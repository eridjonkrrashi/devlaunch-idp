using DevLaunch.Api.Data;
using DevLaunch.Api.Middleware;
using DevLaunch.Api.Services;
using k8s;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;
using Serilog.Formatting.Compact;

// ── Serilog: structured JSON logging ─────────────────────────────────────────
// CreateLogger() (not CreateBootstrapLogger) avoids the ReloadableLogger.Freeze() error
// that occurs when multiple WebApplicationFactory instances start in the same process.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .Enrich.FromLogContext()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .Enrich.WithProperty("app", "devlaunch-idp")
           .WriteTo.Console(new CompactJsonFormatter());
    });

    // ── Database ──────────────────────────────────────────────────────────────
    var dbProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "sqlite";
    if (dbProvider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
    }
    else
    {
        var dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
        Directory.CreateDirectory(dataDir);
        builder.Services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite($"Data Source={Path.Combine(dataDir, "devlaunch.db")}"));
    }

    // ── Kubernetes client ─────────────────────────────────────────────────────
    builder.Services.AddSingleton<IKubernetes>(_ =>
    {
        KubernetesClientConfiguration config;
        if (KubernetesClientConfiguration.IsInCluster())
            config = KubernetesClientConfiguration.InClusterConfig();
        else
            config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
        return new Kubernetes(config);
    });

    // ── Application services ──────────────────────────────────────────────────
    builder.Services.AddScoped<IKubernetesService, KubernetesService>();
    builder.Services.AddScoped<ApplicationService>();
    builder.Services.AddScoped<ProjectService>();
    builder.Services.AddScoped<AuditService>();
    builder.Services.AddHostedService<ReconciliationService>();

    // ── API / MVC ─────────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "DevLaunch IDP API", Version = "v1" });
        c.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Name = "X-API-Key",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
            Description = "API key for authentication (prefix: dlk_...)"
        });
        c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "ApiKey"
                    }
                },
                []
            }
        });
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
    });

    // ── CORS ──────────────────────────────────────────────────────────────────
    builder.Services.AddCors(opts =>
        opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

    // ── Health checks ─────────────────────────────────────────────────────────
    var hcBuilder = builder.Services.AddHealthChecks()
        .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

    if (dbProvider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        hcBuilder.AddNpgSql(builder.Configuration.GetConnectionString("Postgres") ?? "");

    // ── Build ─────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // Auto-migrate on startup
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();

        // Bootstrap: create first admin project + key if no keys exist
        var projectSvc = scope.ServiceProvider.GetRequiredService<ProjectService>();
        var bootstrapKey = await projectSvc.EnsureBootstrapAsync();
        if (bootstrapKey is not null)
        {
            Log.Warning("=================================================================");
            Log.Warning("BOOTSTRAP ADMIN KEY (shown only once — save it now!):");
            Log.Warning("  {Key}", bootstrapKey);
            Log.Warning("=================================================================");
        }
    }

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseSerilogRequestLogging();
    app.UseCors();

    // Global exception handler — consistent JSON error envelope
    app.UseExceptionHandler(exApp =>
    {
        exApp.Run(async ctx =>
        {
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "application/json";
            var feature = ctx.Features.Get<IExceptionHandlerFeature>();
            var ex = feature?.Error;
            Log.Error(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "An unexpected error occurred.",
                correlationId = ctx.TraceIdentifier
            });
        });
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "DevLaunch IDP v1");
            c.RoutePrefix = "swagger";
        });
    }

    app.UseMetricServer();
    app.UseHttpMetrics();
    app.UseStaticFiles();
    app.UseApiKeyAuth(); // ← validate X-API-Key on every non-public route

    // Health + readiness probes (no auth)
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = hc => hc.Tags.Contains("ready") || hc.Name == "self"
    });

    app.MapControllers();
    // Only fallback to index.html if it actually exists (SPA is optional)
    if (File.Exists(Path.Combine(builder.Environment.WebRootPath ?? "", "index.html")))
        app.MapFallbackToFile("index.html");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application startup failed");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
