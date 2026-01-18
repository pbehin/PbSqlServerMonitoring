using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Data;

public class MonitorDbContext : IdentityDbContext<ApplicationUser>
{
    public MonitorDbContext(DbContextOptions<MonitorDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserPreferenceEntity> UserPreferences { get; set; } = null!;
    public DbSet<ServerConnection> ServerConnections { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);



        modelBuilder.Entity<ServerConnection>()
            .HasIndex(c => c.UserId)
            .HasDatabaseName("IX_ServerConnections_UserId");

        modelBuilder.Entity<ServerConnection>()
            .HasIndex(c => c.Status)
            .HasDatabaseName("IX_ServerConnections_Status");

        modelBuilder.Entity<ServerConnection>()
            .HasIndex(c => new { c.UserId, c.IsEnabled })
            .HasDatabaseName("IX_ServerConnections_UserId_IsEnabled");
    }
}
