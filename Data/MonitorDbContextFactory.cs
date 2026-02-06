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

        optionsBuilder.UseSqlServer(
            "Server=.;Database=PbMonitor_Migrations;Trusted_Connection=True;");

        return new MonitorDbContext(optionsBuilder.Options);
    }
}
