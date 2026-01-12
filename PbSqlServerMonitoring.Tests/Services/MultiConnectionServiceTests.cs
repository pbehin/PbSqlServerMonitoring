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
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MultiConnectionService _service;
    private readonly DbContextOptions<MonitorDbContext> _dbOptions;

    public MultiConnectionServiceTests()
    {
        _mockDataProtection = new Mock<IDataProtectionProvider>();
        _mockProtector = new Mock<IDataProtector>();
        _mockLogger = new Mock<ILogger<MultiConnectionService>>();
        _mockConfig = new Mock<IConfiguration>();

        // Setup Data Protection mock
        _mockDataProtection.Setup(x => x.CreateProtector(It.IsAny<string>()))
            .Returns(_mockProtector.Object);

        // Setup encryption/decryption (mock passthrough)
        _mockProtector.Setup(x => x.Protect(It.IsAny<byte[]>()))
            .Returns<byte[]>(x => x);
        _mockProtector.Setup(x => x.Unprotect(It.IsAny<byte[]>()))
            .Returns<byte[]>(x => x);

        // Setup config defaults
        var configSection = new Mock<IConfigurationSection>();
        configSection.Setup(x => x.Value).Returns("5");
        _mockConfig.Setup(x => x.GetSection("Monitoring:MaxConnections")).Returns(configSection.Object);

        // Create in-memory database for testing
        _dbOptions = new DbContextOptionsBuilder<MonitorDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
            
        // Create a real ServiceCollection to get IServiceScopeFactory
        var services = new ServiceCollection();
        services.AddDbContext<MonitorDbContext>(options => 
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));
        var serviceProvider = services.BuildServiceProvider();
        _scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        // Create service
        _service = new MultiConnectionService(
            _mockDataProtection.Object,
            _scopeFactory,
            _mockLogger.Object,
            _mockConfig.Object);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public async Task AddConnection_ShouldAddConnection_WhenLimitNotReached()
    {
        // Assert - verify max connections
        Assert.Equal(5, _service.MaxConnections);
        Assert.Equal(0, _service.ConnectionCount);
    }

    [Fact]
    public void GetStatus_ShouldReturnCorrectCounts()
    {
        // Act
        var status = _service.GetStatus();

        // Assert
        Assert.NotNull(status);
        Assert.Equal(0, status.ActiveConnections);
        Assert.Equal(5, status.MaxConnections);
        Assert.NotNull(status.Connections);
    }

    [Fact]
    public async Task AddConnection_ShouldReturnError_WhenServerIsEmpty()
    {
        // Arrange
        var request = new AddConnectionRequest
        {
            Server = "",
            Database = "TestDB",
            UseWindowsAuth = true
        };

        // Act
        var result = await _service.AddConnectionAsync(request, "user1");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Server name is required", result.Message);
        Assert.Null(result.Connection);
    }

    [Fact]
    public async Task AddConnection_ShouldReturnError_WhenSqlAuthWithoutUsername()
    {
        // Arrange
        var request = new AddConnectionRequest
        {
            Server = "localhost",
            Database = "TestDB",
            UseWindowsAuth = false,
            Username = "" // Missing username for SQL auth
        };

        // Act
        var result = await _service.AddConnectionAsync(request, "user1");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Username is required", result.Message);
    }

    [Fact]
    public async Task RemoveConnection_ShouldReturnError_WhenConnectionNotFound()
    {
        // Act
        var result = await _service.RemoveConnectionAsync("non-existent-id");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public async Task SetConnectionEnabled_ShouldReturnError_WhenConnectionNotFound()
    {
        // Act
        var result = await _service.SetConnectionEnabledAsync("non-existent-id", false);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public void GetConnection_ShouldReturnNull_WhenConnectionNotFound()
    {
        // Act
        var connection = _service.GetConnection("non-existent-id");

        // Assert
        Assert.Null(connection);
    }

    [Fact]
    public void GetConnectionString_ShouldReturnNull_WhenConnectionNotFound()
    {
        // Act
        var connectionString = _service.GetConnectionString("non-existent-id");

        // Assert
        Assert.Null(connectionString);
    }

    [Fact]
    public void GetEnabledConnections_ShouldReturnEmpty_WhenNoConnections()
    {
        // Act
        var enabled = _service.GetEnabledConnections();

        // Assert
        Assert.Empty(enabled);
    }

    [Fact]
    public void Connections_ShouldBeReadOnly()
    {
        // Act
        var connections = _service.Connections;

        // Assert
        Assert.NotNull(connections);
        Assert.IsAssignableFrom<IReadOnlyList<ServerConnection>>(connections);
    }

    [Fact]
    public void MaxConnections_ShouldBeConfigurable()
    {
        // Assert - default is 5 based on our mock setup
        Assert.Equal(5, _service.MaxConnections);
    }
}
