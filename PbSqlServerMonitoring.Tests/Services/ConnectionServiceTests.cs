using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Tests.Services;

/// <summary>
/// Unit tests for ConnectionService
/// </summary>
public class ConnectionServiceTests
{
    private readonly Mock<IDataProtectionProvider> _mockDataProtectionProvider;
    private readonly Mock<IDataProtector> _mockDataProtector;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<ConnectionService>> _mockLogger;

    public ConnectionServiceTests()
    {
        _mockDataProtectionProvider = new Mock<IDataProtectionProvider>();
        _mockDataProtector = new Mock<IDataProtector>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<ConnectionService>>();

        _mockDataProtectionProvider
            .Setup(p => p.CreateProtector(It.IsAny<string>()))
            .Returns(_mockDataProtector.Object);

        // Setup configuration to return null for connection string by default
        _mockConfiguration
            .Setup(c => c.GetSection(It.IsAny<string>()))
            .Returns(new Mock<IConfigurationSection>().Object);
    }

    private ConnectionService CreateService()
    {
        return new ConnectionService(
            _mockDataProtectionProvider.Object,
            _mockConfiguration.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void HasConnection_WhenNoConnectionStored_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        Assert.False(service.HasConnection);
    }

    [Fact]
    public void SetConnectionString_StoresEncryptedConnection()
    {
        // Arrange
        var service = CreateService();
        var connectionString = "Server=localhost;Database=test;";
        var encryptedValue = "encrypted_value";

        _mockDataProtector
            .Setup(p => p.Protect(It.IsAny<byte[]>()))
            .Returns(System.Text.Encoding.UTF8.GetBytes(encryptedValue));

        // Act
        service.SetConnectionString(connectionString);

        // Assert
        Assert.True(service.HasConnection);
        _mockDataProtector.Verify(p => p.Protect(It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void GetConnectionString_WhenNoConnection_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.GetConnectionString();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ClearConnection_RemovesStoredConnection()
    {
        // Arrange
        var service = CreateService();
        var connectionString = "Server=localhost;Database=test;";

        _mockDataProtector
            .Setup(p => p.Protect(It.IsAny<byte[]>()))
            .Returns(System.Text.Encoding.UTF8.GetBytes("encrypted"));

        service.SetConnectionString(connectionString);
        Assert.True(service.HasConnection);

        // Act
        service.ClearConnection();

        // Assert
        Assert.False(service.HasConnection);
    }

    [Fact]
    public void BuildConnectionString_WithWindowsAuth_BuildsCorrectly()
    {
        // Arrange
        var service = CreateService();
        var parameters = new ConnectionParameters
        {
            Server = "localhost",
            Database = "TestDB",
            UseWindowsAuth = true
        };

        // Act
        var result = service.BuildConnectionString(parameters);

        // Assert
        Assert.Contains("Data Source=localhost", result);
        Assert.Contains("Initial Catalog=TestDB", result);
        Assert.Contains("Integrated Security=True", result);
    }

    [Fact]
    public void BuildConnectionString_WithSqlAuth_BuildsCorrectly()
    {
        // Arrange
        var service = CreateService();
        var parameters = new ConnectionParameters
        {
            Server = "localhost",
            Database = "TestDB",
            UseWindowsAuth = false,
            Username = "testuser",
            Password = "testpass"
        };

        // Act
        var result = service.BuildConnectionString(parameters);

        // Assert
        Assert.Contains("Data Source=localhost", result);
        Assert.Contains("User ID=testuser", result);
        Assert.DoesNotContain("Integrated Security=True", result);
    }

    [Fact]
    public void BuildConnectionString_WithEmptyServer_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var parameters = new ConnectionParameters
        {
            Server = "",
            Database = "TestDB",
            UseWindowsAuth = true
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => service.BuildConnectionString(parameters));
        Assert.Contains("Server name is required", ex.Message);
    }

    [Fact]
    public void BuildConnectionString_SqlAuthWithoutUsername_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var parameters = new ConnectionParameters
        {
            Server = "localhost",
            Database = "TestDB",
            UseWindowsAuth = false,
            Username = null,
            Password = "password"
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => service.BuildConnectionString(parameters));
        Assert.Contains("Username is required", ex.Message);
    }

    [Fact]
    public void BuildConnectionString_SqlAuthWithoutPassword_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var parameters = new ConnectionParameters
        {
            Server = "localhost",
            Database = "TestDB",
            UseWindowsAuth = false,
            Username = "user",
            Password = null
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => service.BuildConnectionString(parameters));
        Assert.Contains("Password is required", ex.Message);
    }

    [Theory]
    [InlineData(0, 5)]      // Below minimum, should clamp to 5
    [InlineData(200, 120)]  // Above maximum, should clamp to 120
    [InlineData(30, 30)]    // Normal value, should pass through
    public void BuildConnectionString_ClampsTimeout(int inputTimeout, int expectedTimeout)
    {
        // Arrange
        var service = CreateService();
        var parameters = new ConnectionParameters
        {
            Server = "localhost",
            UseWindowsAuth = true,
            Timeout = inputTimeout
        };

        // Act
        var result = service.BuildConnectionString(parameters);

        // Assert
        Assert.Contains($"Connect Timeout={expectedTimeout}", result);
    }

    [Fact]
    public void GetConnectionInfo_WhenNotConfigured_ReturnsIsConfiguredFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.GetConnectionInfo();

        // Assert
        Assert.False(result.IsConfigured);
    }
}
