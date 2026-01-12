using PbSqlServerMonitoring.Extensions;
using PbSqlServerMonitoring.Configuration;

namespace PbSqlServerMonitoring.Tests.Extensions;

/// <summary>
/// Unit tests for PaginationExtensions
/// </summary>
public class PaginationExtensionsTests
{
    #region ToPagedResult Tests
    
    [Fact]
    public void ToPagedResult_WithEmptyCollection_ReturnsEmptyPage()
    {
        // Arrange
        var source = Enumerable.Empty<int>();
        
        // Act
        var result = source.ToPagedResult(1, 10);
        
        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
    }
    
    [Fact]
    public void ToPagedResult_WithSinglePage_ReturnsAllItems()
    {
        // Arrange
        var source = new[] { 1, 2, 3, 4, 5 };
        
        // Act
        var result = source.ToPagedResult(1, 10);
        
        // Assert
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }
    
    [Fact]
    public void ToPagedResult_WithMultiplePages_ReturnsCorrectPage()
    {
        // Arrange
        var source = Enumerable.Range(1, 25).ToList();
        
        // Act
        var page1 = source.ToPagedResult(1, 10);
        var page2 = source.ToPagedResult(2, 10);
        var page3 = source.ToPagedResult(3, 10);
        
        // Assert
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(1, page1.Items.First());
        Assert.Equal(10, page1.Items.Last());
        
        Assert.Equal(10, page2.Items.Count);
        Assert.Equal(11, page2.Items.First());
        Assert.Equal(20, page2.Items.Last());
        
        Assert.Equal(5, page3.Items.Count);
        Assert.Equal(21, page3.Items.First());
        Assert.Equal(25, page3.Items.Last());
        
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
    }
    
    [Fact]
    public void ToPagedResult_WithProjection_TransformsItems()
    {
        // Arrange
        var source = new[] { 1, 2, 3 };
        
        // Act
        var result = source.ToPagedResult(1, 10, x => x * 2);
        
        // Assert
        Assert.Equal(new[] { 2, 4, 6 }, result.Items);
    }
    
    [Fact]
    public void ToPagedResult_PageBeyondData_ReturnsEmptyItems()
    {
        // Arrange
        var source = new[] { 1, 2, 3 };
        
        // Act
        var result = source.ToPagedResult(10, 10);
        
        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(3, result.TotalCount); // Total count still accurate
        Assert.Equal(1, result.TotalPages);
    }
    
    #endregion
    
    #region ValidatePagination Tests
    
    [Theory]
    [InlineData(0, 10, 1, 10)]       // Page 0 becomes 1
    [InlineData(-5, 10, 1, 10)]      // Negative page becomes 1
    [InlineData(1, 0, 1, 1)]         // PageSize 0 becomes 1
    [InlineData(1, -10, 1, 1)]       // Negative pageSize becomes 1
    [InlineData(1, 5000, 1, 1000)]   // PageSize exceeds max, clamped to 1000
    [InlineData(5, 50, 5, 50)]       // Valid values pass through
    public void ValidatePagination_ClampsValuesProperly(int inputPage, int inputPageSize, int expectedPage, int expectedPageSize)
    {
        // Act
        var (page, pageSize) = PaginationExtensions.ValidatePagination(inputPage, inputPageSize);
        
        // Assert
        Assert.Equal(expectedPage, page);
        Assert.Equal(expectedPageSize, pageSize);
    }
    
    [Fact]
    public void ValidatePagination_WithCustomMaxPageSize_ClampsCorrectly()
    {
        // Act
        var (_, pageSize) = PaginationExtensions.ValidatePagination(1, 100, maxPageSize: 25);
        
        // Assert
        Assert.Equal(25, pageSize);
    }
    
    #endregion
    
    #region ValidateTopN Tests
    
    [Theory]
    [InlineData(0, 1)]       // 0 becomes 1
    [InlineData(-5, 1)]      // Negative becomes 1
    [InlineData(50, 50)]     // Valid passes through
    [InlineData(500, 100)]   // Exceeds max, clamped to 100
    public void ValidateTopN_ClampsValuesProperly(int input, int expected)
    {
        // Act
        var result = PaginationExtensions.ValidateTopN(input);
        
        // Assert
        Assert.Equal(expected, result);
    }
    
    [Fact]
    public void ValidateTopN_WithCustomMax_ClampsCorrectly()
    {
        // Act
        var result = PaginationExtensions.ValidateTopN(50, maxN: 10);
        
        // Assert
        Assert.Equal(10, result);
    }
    
    #endregion
    
    #region ValidateRangeSeconds Tests
    
    [Theory]
    [InlineData(0, MetricsConstants.MinRangeSeconds)]      // Below min
    [InlineData(-100, MetricsConstants.MinRangeSeconds)]   // Negative
    [InlineData(60, 60)]                                    // Valid
    [InlineData(500000, MetricsConstants.MaxRangeSeconds)] // Above max
    public void ValidateRangeSeconds_ClampsValuesProperly(int input, int expected)
    {
        // Act
        var result = PaginationExtensions.ValidateRangeSeconds(input);
        
        // Assert
        Assert.Equal(expected, result);
    }
    
    #endregion
}
