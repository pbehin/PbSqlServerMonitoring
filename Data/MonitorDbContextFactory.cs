using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PbSqlServerMonitoring.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// This allows migrations to be created without running the full application.
/// </summary>
public class MonitorDbContextFactory : IDesignTimeDbContextFactory<MonitorDbContext>
{
    public MonitorDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MonitorDbContext>();
        
        // Use a default connection string for migrations
        // This will be overridden at runtime by the actual configuration
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=PbMonitor_Migrations;Trusted_Connection=True;");
        
        return new MonitorDbContext(optionsBuilder.Options);
    }
}
