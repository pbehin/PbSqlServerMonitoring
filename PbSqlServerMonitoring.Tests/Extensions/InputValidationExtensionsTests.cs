using PbSqlServerMonitoring.Extensions;
using Xunit;

namespace PbSqlServerMonitoring.Tests.Extensions;

/// <summary>
/// Unit tests for InputValidationExtensions.
/// Covers edge cases and security-related validation scenarios.
/// </summary>
public class InputValidationExtensionsTests
{
    #region ValidateConnectionId Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateConnectionId_NullOrEmpty_ReturnsInvalid(string? connectionId)
    {
        // Act
        var (isValid, sanitizedId, error) = InputValidationExtensions.ValidateConnectionId(connectionId);

        // Assert
        Assert.False(isValid);
        Assert.Null(sanitizedId);
        Assert.Equal("Connection ID is required", error);
    }

    [Theory]
    [InlineData("abc")]          // Too short (< 8)
    [InlineData("12345")]        // Too short
    [InlineData("1234567")]      // 7 chars - just under limit
    public void ValidateConnectionId_TooShort_ReturnsInvalid(string connectionId)
    {
        // Act
        var (isValid, sanitizedId, error) = InputValidationExtensions.ValidateConnectionId(connectionId);

        // Assert
        Assert.False(isValid);
        Assert.Null(sanitizedId);
        Assert.Equal("Invalid Connection ID format", error);
    }

    [Theory]
    [InlineData("12345678901234567")] // 17 chars - over limit
    [InlineData("12345678901234567890")]
    public void ValidateConnectionId_TooLong_ReturnsInvalid(string connectionId)
    {
        // Act
        var (isValid, sanitizedId, error) = InputValidationExtensions.ValidateConnectionId(connectionId);

        // Assert
        Assert.False(isValid);
        Assert.Null(sanitizedId);
        Assert.Equal("Invalid Connection ID format", error);
    }

    [Theory]
    [InlineData("1234567g")]      // Contains 'g' (not hex)
    [InlineData("abcd-efg")]      // Contains hyphen and 'g'
    [InlineData("12 345678")]     // Contains embedded space
    [InlineData("!@#$%^&*")]      // Special characters
    [InlineData("SELECT * ")]     // SQL injection attempt (note: trailing space gets trimmed, but contains non-hex)
    public void ValidateConnectionId_InvalidCharacters_ReturnsInvalid(string connectionId)
    {
        // Act
        var (isValid, sanitizedId, error) = InputValidationExtensions.ValidateConnectionId(connectionId);

        // Assert
        Assert.False(isValid);
        Assert.Null(sanitizedId);
        Assert.Equal("Invalid Connection ID format", error);
    }

    [Theory]
    [InlineData("12345678", "12345678")]           // 8 chars - minimum valid
    [InlineData("abcdef1234567890", "abcdef1234567890")] // 16 chars - maximum valid
    [InlineData("ABCDEF12", "abcdef12")]           // Uppercase - should lowercase
    [InlineData("AbCdEf12345678", "abcdef12345678")] // Mixed case
    public void ValidateConnectionId_ValidHex_ReturnsValid(string connectionId, string expectedSanitized)
    {
        // Act
        var (isValid, sanitizedId, error) = InputValidationExtensions.ValidateConnectionId(connectionId);

        // Assert
        Assert.True(isValid);
        Assert.Equal(expectedSanitized, sanitizedId);
        Assert.Null(error);
    }

    #endregion

    #region ValidateServerName Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateServerName_NullOrEmpty_ReturnsInvalid(string? server)
    {
        // Act
        var (isValid, sanitizedServer, error) = InputValidationExtensions.ValidateServerName(server);

        // Assert
        Assert.False(isValid);
        Assert.Null(sanitizedServer);
        Assert.Equal("Server name is required", error);
    }

    [Fact]
    public void ValidateServerName_TooLong_ReturnsInvalid()
    {
        // Arrange
        var server = new string('a', 257);

        // Act
        var (isValid, sanitizedServer, error) = InputValidationExtensions.ValidateServerName(server);

        // Assert
        Assert.False(isValid);
        Assert.Null(sanitizedServer);
        Assert.Equal("Server name is too long", error);
    }

    [Theory]
    [InlineData("server;DROP TABLE Users")]         // SQL injection with semicolon
    [InlineData("server'--")]                       // SQL injection with quote
    [InlineData("server\"")]                        // Double quote
    [InlineData("localhost;")]                      // Trailing semicolon
    public void ValidateServerName_InjectionAttempts_ReturnsInvalid(string server)
    {
        // Act
        var (isValid, sanitizedServer, error) = InputValidationExtensions.ValidateServerName(server);

        // Assert
        Assert.False(isValid);
        Assert.Null(sanitizedServer);
        Assert.Equal("Invalid characters in server name", error);
    }

    [Theory]
    [InlineData("localhost", "localhost")]
    [InlineData("192.168.1.1", "192.168.1.1")]
    [InlineData("server\\instance", "server\\instance")]  // Named instance
    [InlineData("server,1433", "server,1433")]            // Port syntax
    [InlineData("  server  ", "server")]                  // Trimming
    public void ValidateServerName_ValidServers_ReturnsValid(string server, string expected)
    {
        // Act
        var (isValid, sanitizedServer, error) = InputValidationExtensions.ValidateServerName(server);

        // Assert
        Assert.True(isValid);
        Assert.Equal(expected, sanitizedServer);
        Assert.Null(error);
    }

    #endregion

    #region ValidateDatabaseName Tests

    [Theory]
    [InlineData(null, "master")]
    [InlineData("", "master")]
    [InlineData("   ", "master")]
    public void ValidateDatabaseName_NullOrEmpty_ReturnsDefaultMaster(string? database, string expected)
    {
        // Act
        var (isValid, sanitizedDb, error) = InputValidationExtensions.ValidateDatabaseName(database);

        // Assert
        Assert.True(isValid);
        Assert.Equal(expected, sanitizedDb);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateDatabaseName_TooLong_ReturnsInvalid()
    {
        // Arrange - SQL Server max is 128
        var database = new string('a', 129);

        // Act
        var (isValid, sanitizedDb, error) = InputValidationExtensions.ValidateDatabaseName(database);

        // Assert
        Assert.False(isValid);
        Assert.Null(sanitizedDb);
        Assert.Equal("Database name is too long", error);
    }

    [Theory]
    [InlineData("db;DROP TABLE Users")]     // SQL injection
    [InlineData("db'--")]                   // Quote injection
    [InlineData("db\"")]                    // Double quote
    [InlineData("[dbo]")]                   // Square brackets
    [InlineData("db]")]                     // Closing bracket
    public void ValidateDatabaseName_InjectionAttempts_ReturnsInvalid(string database)
    {
        // Act
        var (isValid, sanitizedDb, error) = InputValidationExtensions.ValidateDatabaseName(database);

        // Assert
        Assert.False(isValid);
        Assert.Null(sanitizedDb);
        Assert.Equal("Invalid characters in database name", error);
    }

    [Theory]
    [InlineData("MyDatabase", "MyDatabase")]
    [InlineData("Production_DB", "Production_DB")]
    [InlineData("  TestDB  ", "TestDB")]    // Trimming
    public void ValidateDatabaseName_ValidNames_ReturnsValid(string database, string expected)
    {
        // Act
        var (isValid, sanitizedDb, error) = InputValidationExtensions.ValidateDatabaseName(database);

        // Assert
        Assert.True(isValid);
        Assert.Equal(expected, sanitizedDb);
        Assert.Null(error);
    }

    #endregion

    #region ValidateUsername Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateUsername_NullOrEmpty_ReturnsInvalid(string? username)
    {
        // Act
        var (isValid, sanitizedUser, error) = InputValidationExtensions.ValidateUsername(username);

        // Assert
        Assert.False(isValid);
        Assert.Null(sanitizedUser);
        Assert.Equal("Username is required for SQL authentication", error);
    }

    [Fact]
    public void ValidateUsername_TooLong_ReturnsInvalid()
    {
        // Arrange
        var username = new string('a', 129);

        // Act
        var (isValid, sanitizedUser, error) = InputValidationExtensions.ValidateUsername(username);

        // Assert
        Assert.False(isValid);
        Assert.Null(sanitizedUser);
        Assert.Equal("Username is too long", error);
    }

    [Theory]
    [InlineData("user;DROP TABLE")]         // SQL injection
    [InlineData("user'--")]                 // Quote injection
    [InlineData("user\"")]                  // Double quote
    public void ValidateUsername_InjectionAttempts_ReturnsInvalid(string username)
    {
        // Act
        var (isValid, sanitizedUser, error) = InputValidationExtensions.ValidateUsername(username);

        // Assert
        Assert.False(isValid);
        Assert.Null(sanitizedUser);
        Assert.Equal("Invalid characters in username", error);
    }

    [Theory]
    [InlineData("sa", "sa")]
    [InlineData("db_admin", "db_admin")]
    [InlineData("  admin  ", "admin")]      // Trimming
    [InlineData("user@domain.com", "user@domain.com")]  // Email format
    public void ValidateUsername_ValidNames_ReturnsValid(string username, string expected)
    {
        // Act
        var (isValid, sanitizedUser, error) = InputValidationExtensions.ValidateUsername(username);

        // Assert
        Assert.True(isValid);
        Assert.Equal(expected, sanitizedUser);
        Assert.Null(error);
    }

    #endregion

    #region ValidateTimeout Tests

    [Theory]
    [InlineData(-1, 5)]      // Below minimum, should clamp to 5
    [InlineData(0, 5)]       // Zero, should clamp to 5
    [InlineData(3, 5)]       // Below minimum, should clamp to 5
    [InlineData(5, 5)]       // Exactly minimum
    [InlineData(30, 30)]     // Normal value
    [InlineData(120, 120)]   // Exactly maximum
    [InlineData(200, 120)]   // Above maximum, should clamp to 120
    public void ValidateTimeout_ClampsToValidRange(int input, int expected)
    {
        // Act
        var result = InputValidationExtensions.ValidateTimeout(input);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion
}
