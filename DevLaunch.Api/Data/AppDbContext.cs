using DevLaunch.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DevLaunch.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Models.Application> Applications => Set<Models.Application>();
    public DbSet<DeploymentRevision> DeploymentRevisions => Set<DeploymentRevision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Models.Application>(app =>
        {
            app.HasKey(a => a.Id);
            app.HasIndex(a => a.Name).IsUnique();
            app.Property(a => a.Status).HasConversion<string>();
            // Store EnvVars as a JSON column
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
    }
}
