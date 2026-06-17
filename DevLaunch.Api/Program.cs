using DevLaunch.Api.Data;
using DevLaunch.Api.Services;
using k8s;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;
using Serilog.Formatting.Compact;

// ── Serilog: structured JSON logging ─────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .Enrich.FromLogContext()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
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
    // Auto-detects: in-cluster ServiceAccount token first, then local kubeconfig.
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
    builder.Services.AddHostedService<ReconciliationService>();

    // ── API / MVC ─────────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "DevLaunch IDP API", Version = "v1" });
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
    });

    // ── CORS (allow dev frontend) ─────────────────────────────────────────────
    builder.Services.AddCors(opts =>
        opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

    // ── Health checks ─────────────────────────────────────────────────────────
    var hcBuilder = builder.Services.AddHealthChecks()
        .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

    if (dbProvider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
    {
        hcBuilder.AddNpgSql(builder.Configuration.GetConnectionString("Postgres") ?? "");
    }

    // ── Build ─────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // Auto-migrate on startup
    using (var scope = app.Services.CreateScope())
    {
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.Database.Migrate();
    }

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseSerilogRequestLogging();
    app.UseCors();
    app.UseHttpsRedirection();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "DevLaunch IDP v1");
            c.RoutePrefix = "swagger";
        });
    }

    // Prometheus metrics scrape endpoint
    app.UseMetricServer();
    app.UseHttpMetrics();

    app.UseStaticFiles();

    // Health & readiness probes
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = hc => hc.Tags.Contains("ready") || hc.Name == "self"
    });

    // Serve the React SPA from wwwroot for non-API routes
    app.MapControllers();
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
