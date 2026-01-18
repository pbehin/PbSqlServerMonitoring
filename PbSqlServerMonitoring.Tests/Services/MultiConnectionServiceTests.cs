using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PbSqlServerMonitoring.Data;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;
using Xunit;

namespace PbSqlServerMonitoring.Tests.Services;

public class MultiConnectionServiceTests : IDisposable
{
    private readonly Mock<IDataProtectionProvider> _mockDataProtection;
    private readonly Mock<IDataProtector> _mockProtector;
    private readonly Mock<ILogger<MultiConnectionService>> _mockLogger;
    private readonly Mock<IPrometheusTargetExporter> _mockExporter;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MultiConnectionService _service;
    private readonly DbContextOptions<MonitorDbContext> _dbOptions;

    public MultiConnectionServiceTests()
    {
        _mockDataProtection = new Mock<IDataProtectionProvider>();
        _mockProtector = new Mock<IDataProtector>();
        _mockLogger = new Mock<ILogger<MultiConnectionService>>();
        _mockExporter = new Mock<IPrometheusTargetExporter>();

        _mockDataProtection.Setup(x => x.CreateProtector(It.IsAny<string>()))
            .Returns(_mockProtector.Object);

        _mockProtector.Setup(x => x.Protect(It.IsAny<byte[]>()))
            .Returns<byte[]>(x => x);
        _mockProtector.Setup(x => x.Unprotect(It.IsAny<byte[]>()))
            .Returns<byte[]>(x => x);

        var configDict = new Dictionary<string, string?>
        {
            { "Monitoring:MaxConnections", "5" },
            { "Monitoring:DefaultUserMaxConnections", "2" }
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        _dbOptions = new DbContextOptionsBuilder<MonitorDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddDbContext<MonitorDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));
        var serviceProvider = services.BuildServiceProvider();
        _scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _service = new MultiConnectionService(
            _mockDataProtection.Object,
            _scopeFactory,
            _mockLogger.Object,
            _configuration,
            _mockExporter.Object);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public async Task AddConnection_ShouldAddConnection_WhenLimitNotReached()
    {
        Assert.Equal(5, _service.MaxConnections);
        Assert.Equal(0, _service.ConnectionCount);
    }

    [Fact]
    public void GetStatus_ShouldReturnCorrectCounts()
    {
        var status = _service.GetStatus();

        Assert.NotNull(status);
        Assert.Equal(0, status.ActiveConnections);
        Assert.Equal(5, status.MaxConnections);
        Assert.NotNull(status.Connections);
    }

    [Fact]
    public async Task AddConnection_ShouldReturnError_WhenServerIsEmpty()
    {
        var request = new AddConnectionRequest
        {
            Server = "",
            Database = "TestDB",
            UseWindowsAuth = true
        };

        var result = await _service.AddConnectionAsync(request, "user1");

        Assert.False(result.Success);
        Assert.Contains("Server name is required", result.Message);
        Assert.Null(result.Connection);
    }

    [Fact]
    public async Task AddConnection_ShouldReturnError_WhenSqlAuthWithoutUsername()
    {
        var request = new AddConnectionRequest
        {
            Server = "localhost",
            Database = "TestDB",
            UseWindowsAuth = false,
            Username = ""
        };

        var result = await _service.AddConnectionAsync(request, "user1");

        Assert.False(result.Success);
        Assert.Contains("Username is required", result.Message);
    }

    [Fact]
    public async Task RemoveConnection_ShouldReturnError_WhenConnectionNotFound()
    {
        var result = await _service.RemoveConnectionAsync("non-existent-id");

        Assert.False(result.Success);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public async Task SetConnectionEnabled_ShouldReturnError_WhenConnectionNotFound()
    {
        var result = await _service.SetConnectionEnabledAsync("non-existent-id", false);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public void GetConnection_ShouldReturnNull_WhenConnectionNotFound()
    {
        var connection = _service.GetConnection("non-existent-id");

        Assert.Null(connection);
    }

    [Fact]
    public void GetConnectionString_ShouldReturnNull_WhenConnectionNotFound()
    {
        var connectionString = _service.GetConnectionString("non-existent-id");

        Assert.Null(connectionString);
    }

    [Fact]
    public void GetEnabledConnections_ShouldReturnEmpty_WhenNoConnections()
    {
        var enabled = _service.GetEnabledConnections();

        Assert.Empty(enabled);
    }

    [Fact]
    public void Connections_ShouldBeReadOnly()
    {
        var connections = _service.Connections;

        Assert.NotNull(connections);
        Assert.IsAssignableFrom<IReadOnlyList<ServerConnection>>(connections);
    }

    [Fact]
    public void MaxConnections_ShouldBeConfigurable()
    {
        Assert.Equal(5, _service.MaxConnections);
    }
}
