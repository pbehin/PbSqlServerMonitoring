using PbSqlServerMonitoring.Extensions;
using Xunit;

namespace PbSqlServerMonitoring.Tests.Extensions;

/// <summary>
/// Unit tests for InputValidationExtensions.
/// Covers edge cases and security-related validation scenarios.
/// </summary>
public class InputValidationExtensionsTests
{

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateConnectionId_NullOrEmpty_ReturnsInvalid(string? connectionId)
    {
        var (isValid, sanitizedId, error) = InputValidationExtensions.ValidateConnectionId(connectionId);

        Assert.False(isValid);
        Assert.Null(sanitizedId);
        Assert.Equal("Connection ID is required", error);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    [InlineData("1234567")]
    public void ValidateConnectionId_TooShort_ReturnsInvalid(string connectionId)
    {
        var (isValid, sanitizedId, error) = InputValidationExtensions.ValidateConnectionId(connectionId);

        Assert.False(isValid);
        Assert.Null(sanitizedId);
        Assert.Equal("Invalid Connection ID format", error);
    }

    [Theory]
    [InlineData("12345678901234567")]
    [InlineData("12345678901234567890")]
    public void ValidateConnectionId_TooLong_ReturnsInvalid(string connectionId)
    {
        var (isValid, sanitizedId, error) = InputValidationExtensions.ValidateConnectionId(connectionId);

        Assert.False(isValid);
        Assert.Null(sanitizedId);
        Assert.Equal("Invalid Connection ID format", error);
    }

    [Theory]
    [InlineData("1234567g")]
    [InlineData("abcd-efg")]
    [InlineData("12 345678")]
    [InlineData("!@#$%^&*")]
    [InlineData("SELECT * ")]
    public void ValidateConnectionId_InvalidCharacters_ReturnsInvalid(string connectionId)
    {
        var (isValid, sanitizedId, error) = InputValidationExtensions.ValidateConnectionId(connectionId);

        Assert.False(isValid);
        Assert.Null(sanitizedId);
        Assert.Equal("Invalid Connection ID format", error);
    }

    [Theory]
    [InlineData("12345678", "12345678")]
    [InlineData("abcdef1234567890", "abcdef1234567890")]
    [InlineData("ABCDEF12", "abcdef12")]
    [InlineData("AbCdEf12345678", "abcdef12345678")]
    public void ValidateConnectionId_ValidHex_ReturnsValid(string connectionId, string expectedSanitized)
    {
        var (isValid, sanitizedId, error) = InputValidationExtensions.ValidateConnectionId(connectionId);

        Assert.True(isValid);
        Assert.Equal(expectedSanitized, sanitizedId);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateServerName_NullOrEmpty_ReturnsInvalid(string? server)
    {
        var (isValid, sanitizedServer, error) = InputValidationExtensions.ValidateServerName(server);

        Assert.False(isValid);
        Assert.Null(sanitizedServer);
        Assert.Equal("Server name is required", error);
    }

    [Fact]
    public void ValidateServerName_TooLong_ReturnsInvalid()
    {
        var server = new string('a', 257);

        var (isValid, sanitizedServer, error) = InputValidationExtensions.ValidateServerName(server);

        Assert.False(isValid);
        Assert.Null(sanitizedServer);
        Assert.Equal("Server name is too long", error);
    }

    [Theory]
    [InlineData("server;DROP TABLE Users")]
    [InlineData("server'--")]
    [InlineData("server\"")]
    [InlineData("localhost;")]
    public void ValidateServerName_InjectionAttempts_ReturnsInvalid(string server)
    {
        var (isValid, sanitizedServer, error) = InputValidationExtensions.ValidateServerName(server);

        Assert.False(isValid);
        Assert.Null(sanitizedServer);
        Assert.Equal("Invalid characters in server name", error);
    }

    [Theory]
    [InlineData("localhost", "localhost")]
    [InlineData("192.168.1.1", "192.168.1.1")]
    [InlineData("server\\instance", "server\\instance")]
    [InlineData("server,1433", "server,1433")]
    [InlineData("  server  ", "server")]
    public void ValidateServerName_ValidServers_ReturnsValid(string server, string expected)
    {
        var (isValid, sanitizedServer, error) = InputValidationExtensions.ValidateServerName(server);

        Assert.True(isValid);
        Assert.Equal(expected, sanitizedServer);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null, "master")]
    [InlineData("", "master")]
    [InlineData("   ", "master")]
    public void ValidateDatabaseName_NullOrEmpty_ReturnsDefaultMaster(string? database, string expected)
    {
        var (isValid, sanitizedDb, error) = InputValidationExtensions.ValidateDatabaseName(database);

        Assert.True(isValid);
        Assert.Equal(expected, sanitizedDb);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateDatabaseName_TooLong_ReturnsInvalid()
    {
        var database = new string('a', 129);

        var (isValid, sanitizedDb, error) = InputValidationExtensions.ValidateDatabaseName(database);

        Assert.False(isValid);
        Assert.Null(sanitizedDb);
        Assert.Equal("Database name is too long", error);
    }

    [Theory]
    [InlineData("db;DROP TABLE Users")]
    [InlineData("db'--")]
    [InlineData("db\"")]
    [InlineData("[dbo]")]
    [InlineData("db]")]
    public void ValidateDatabaseName_InjectionAttempts_ReturnsInvalid(string database)
    {
        var (isValid, sanitizedDb, error) = InputValidationExtensions.ValidateDatabaseName(database);

        Assert.False(isValid);
        Assert.Null(sanitizedDb);
        Assert.Equal("Invalid characters in database name", error);
    }

    [Theory]
    [InlineData("MyDatabase", "MyDatabase")]
    [InlineData("Production_DB", "Production_DB")]
    [InlineData("  TestDB  ", "TestDB")]
    public void ValidateDatabaseName_ValidNames_ReturnsValid(string database, string expected)
    {
        var (isValid, sanitizedDb, error) = InputValidationExtensions.ValidateDatabaseName(database);

        Assert.True(isValid);
        Assert.Equal(expected, sanitizedDb);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateUsername_NullOrEmpty_ReturnsInvalid(string? username)
    {
        var (isValid, sanitizedUser, error) = InputValidationExtensions.ValidateUsername(username);

        Assert.False(isValid);
        Assert.Null(sanitizedUser);
        Assert.Equal("Username is required for SQL authentication", error);
    }

    [Fact]
    public void ValidateUsername_TooLong_ReturnsInvalid()
    {
        var username = new string('a', 129);

        var (isValid, sanitizedUser, error) = InputValidationExtensions.ValidateUsername(username);

        Assert.False(isValid);
        Assert.Null(sanitizedUser);
        Assert.Equal("Username is too long", error);
    }

    [Theory]
    [InlineData("user;DROP TABLE")]
    [InlineData("user'--")]
    [InlineData("user\"")]
    public void ValidateUsername_InjectionAttempts_ReturnsInvalid(string username)
    {
        var (isValid, sanitizedUser, error) = InputValidationExtensions.ValidateUsername(username);

        Assert.False(isValid);
        Assert.Null(sanitizedUser);
        Assert.Equal("Invalid characters in username", error);
    }

    [Theory]
    [InlineData("sa", "sa")]
    [InlineData("db_admin", "db_admin")]
    [InlineData("  admin  ", "admin")]
    [InlineData("user@domain.com", "user@domain.com")]
    public void ValidateUsername_ValidNames_ReturnsValid(string username, string expected)
    {
        var (isValid, sanitizedUser, error) = InputValidationExtensions.ValidateUsername(username);

        Assert.True(isValid);
        Assert.Equal(expected, sanitizedUser);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(0, 5)]
    [InlineData(3, 5)]
    [InlineData(5, 5)]
    [InlineData(30, 30)]
    [InlineData(120, 120)]
    [InlineData(200, 120)]
    public void ValidateTimeout_ClampsToValidRange(int input, int expected)
    {
        var result = InputValidationExtensions.ValidateTimeout(input);

        Assert.Equal(expected, result);
    }

}
