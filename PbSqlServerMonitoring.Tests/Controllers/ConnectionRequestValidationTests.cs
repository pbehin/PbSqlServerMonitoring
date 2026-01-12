using System.ComponentModel.DataAnnotations;
using PbSqlServerMonitoring.Controllers;

namespace PbSqlServerMonitoring.Tests.Controllers;

/// <summary>
/// Unit tests for ConnectionRequest validation
/// </summary>
public class ConnectionRequestValidationTests
{
    private IList<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var ctx = new ValidationContext(model, null, null);
        Validator.TryValidateObject(model, ctx, validationResults, true);
        return validationResults;
    }

    [Fact]
    public void ConnectionRequest_WithValidData_PassesValidation()
    {
        // Arrange
        var request = new ConnectionRequest
        {
            Server = "localhost",
            Database = "master",
            UseWindowsAuth = true,
            Timeout = 30
        };

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void ConnectionRequest_WithNullServer_FailsValidation()
    {
        // Arrange
        var request = new ConnectionRequest
        {
            Server = null,
            Database = "master"
        };

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.Single(results);
        Assert.Contains("Server name is required", results[0].ErrorMessage);
    }

    [Fact]
    public void ConnectionRequest_WithEmptyServer_FailsValidation()
    {
        // Arrange
        var request = new ConnectionRequest
        {
            Server = "",
            Database = "master"
        };

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.NotEmpty(results);
    }

    [Theory]
    [InlineData(4)]   // Below minimum
    [InlineData(121)] // Above maximum
    public void ConnectionRequest_WithInvalidTimeout_FailsValidation(int timeout)
    {
        // Arrange
        var request = new ConnectionRequest
        {
            Server = "localhost",
            Timeout = timeout
        };

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.MemberNames.Contains("Timeout"));
    }

    [Theory]
    [InlineData(5)]   // Minimum valid
    [InlineData(60)]  // Mid-range
    [InlineData(120)] // Maximum valid
    public void ConnectionRequest_WithValidTimeout_PassesValidation(int timeout)
    {
        // Arrange
        var request = new ConnectionRequest
        {
            Server = "localhost",
            Timeout = timeout
        };

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void ConnectionRequest_WithVeryLongServer_FailsValidation()
    {
        // Arrange
        var request = new ConnectionRequest
        {
            Server = new string('x', 300), // Exceeds 256 limit
            Database = "master"
        };

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.MemberNames.Contains("Server"));
    }
}
