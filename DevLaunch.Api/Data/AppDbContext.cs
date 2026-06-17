using DevLaunch.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DevLaunch.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Models.Application> Applications => Set<Models.Application>();
    public DbSet<DeploymentRevision> DeploymentRevisions => Set<DeploymentRevision>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(p =>
        {
            p.HasKey(x => x.Id);
            p.HasIndex(x => x.Name).IsUnique();
            p.HasIndex(x => x.Namespace).IsUnique();
            p.HasMany(x => x.ApiKeys).WithOne(k => k.Project).HasForeignKey(k => k.ProjectId).OnDelete(DeleteBehavior.Cascade);
            p.HasMany(x => x.Applications).WithOne(a => a.Project).HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Cascade);
            p.HasMany(x => x.AuditEntries).WithOne(e => e.Project).HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiKey>(k =>
        {
            k.HasKey(x => x.Id);
            k.HasIndex(x => x.KeyHash).IsUnique();
            k.Property(x => x.Role).HasConversion<string>();
        });

        modelBuilder.Entity<Models.Application>(app =>
        {
            app.HasKey(a => a.Id);
            app.HasIndex(a => new { a.ProjectId, a.Name }).IsUnique();
            app.Property(a => a.Status).HasConversion<string>();
            app.Property(a => a.RolloutPhase).HasConversion<string>();
            app.Property(a => a.EnvVars)
               .HasConversion(
                   v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                   v => JsonSerializer.Deserialize<List<EnvVar>>(v, (JsonSerializerOptions?)null) ?? new())
               .HasColumnType("text");
            app.HasMany(a => a.Revisions)
               .WithOne(r => r.Application)
               .HasForeignKey(r => r.ApplicationId)
               .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeploymentRevision>(rev =>
        {
            rev.HasKey(r => r.Id);
            rev.HasIndex(r => new { r.ApplicationId, r.RevisionNumber }).IsUnique();
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.ProjectId);
        });
    }
}
