using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Query.Results;

namespace Pragmatic.Persistence.Tests.QueryTests;

/// <summary>
///     Tests for PagedResult functionality.
/// </summary>
public class PagedResultTests
{
    #region Test Entity

    private class Order
    {
        public Guid Id { get; set; }
        public string OrderNumber { get; set; } = "";
        public decimal Total { get; set; }
    }

    #endregion

    #region Success Factory Tests

    [Fact]
    public void Success_WithItems_CreatesSuccessResult()
    {
        // Arrange
        var items = new List<Order>
        {
            new() { Id = Guid.NewGuid(), OrderNumber = "ORD-001", Total = 100m },
            new() { Id = Guid.NewGuid(), OrderNumber = "ORD-002", Total = 200m }
        };

        // Act
        var result = PagedResult<Order>.Success(items, totalCount: 10, page: 1, pageSize: 2);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(10);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public void Success_EmptyItems_CreatesSuccessResult()
    {
        // Act
        var result = PagedResult<Order>.Success([], totalCount: 0, page: 1, pageSize: 10);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    #endregion

    #region Failure Factory Tests

    [Fact]
    public void Failure_WithError_CreatesFailureResult()
    {
        // Arrange
        var error = new QueryError.Database { Message = "Database connection failed" };

        // Act
        var result = PagedResult<Order>.Failure(error);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    #endregion

    #region TotalPages Calculation Tests

    [Theory]
    [InlineData(10, 5, 2)]   // 10 items, page size 5 = 2 pages
    [InlineData(11, 5, 3)]   // 11 items, page size 5 = 3 pages (ceiling)
    [InlineData(0, 5, 0)]    // 0 items = 0 pages
    [InlineData(5, 5, 1)]    // Exactly one page
    [InlineData(1, 10, 1)]   // Less than page size = 1 page
    public void TotalPages_CalculatesCorrectly(int totalCount, int pageSize, int expectedPages)
    {
        // Act
        var result = PagedResult<Order>.Success([], totalCount, page: 1, pageSize);

        // Assert
        result.TotalPages.Should().Be(expectedPages);
    }

    #endregion

    #region HasNextPage/HasPreviousPage Tests

    [Fact]
    public void HasNextPage_OnFirstPageWithMorePages_ReturnsTrue()
    {
        // Act
        var result = PagedResult<Order>.Success([], totalCount: 10, page: 1, pageSize: 5);

        // Assert
        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void HasNextPage_OnLastPage_ReturnsFalse()
    {
        // Act
        var result = PagedResult<Order>.Success([], totalCount: 10, page: 2, pageSize: 5);

        // Assert
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void HasPreviousPage_OnFirstPage_ReturnsFalse()
    {
        // Act
        var result = PagedResult<Order>.Success([], totalCount: 10, page: 1, pageSize: 5);

        // Assert
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void HasPreviousPage_OnSecondPage_ReturnsTrue()
    {
        // Act
        var result = PagedResult<Order>.Success([], totalCount: 10, page: 2, pageSize: 5);

        // Assert
        result.HasPreviousPage.Should().BeTrue();
    }

    #endregion

    #region Match Tests

    [Fact]
    public void Match_OnSuccess_CallsSuccessHandler()
    {
        // Arrange
        var items = new List<Order> { new() { OrderNumber = "ORD-001" } };
        var result = PagedResult<Order>.Success(items, totalCount: 1, page: 1, pageSize: 10);

        // Act
        var message = result.Match(
            success: (i, total) => $"Found {i.Count} of {total}",
            failure: error => $"Error: {error.Code}");

        // Assert
        message.Should().Be("Found 1 of 1");
    }

    [Fact]
    public void Match_OnFailure_CallsFailureHandler()
    {
        // Arrange
        var error = new QueryError.Database { Message = "Something went wrong" };
        var result = PagedResult<Order>.Failure(error);

        // Act
        var message = result.Match(
            success: (items, total) => $"Found {items.Count}",
            failure: e => $"Error: {e.Code}");

        // Assert
        message.Should().Be("Error: QUERY_DATABASE_ERROR");
    }

    #endregion

    #region Select Tests

    [Fact]
    public void Select_OnSuccess_TransformsItems()
    {
        // Arrange
        var items = new List<Order>
        {
            new() { OrderNumber = "ORD-001", Total = 100m },
            new() { OrderNumber = "ORD-002", Total = 200m }
        };
        var result = PagedResult<Order>.Success(items, totalCount: 2, page: 1, pageSize: 10);

        // Act
        var mapped = result.Select(o => o.OrderNumber);

        // Assert
        mapped.IsSuccess.Should().BeTrue();
        mapped.Items.Should().BeEquivalentTo(["ORD-001", "ORD-002"]);
        mapped.TotalCount.Should().Be(2);
    }

    [Fact]
    public void Select_OnFailure_PreservesError()
    {
        // Arrange
        var error = new QueryError.Database { Message = "Failed" };
        var result = PagedResult<Order>.Failure(error);

        // Act
        var mapped = result.Select(o => o.OrderNumber);

        // Assert
        mapped.IsFailure.Should().BeTrue();
        mapped.Error!.Code.Should().Be("QUERY_DATABASE_ERROR");
    }

    #endregion

    #region Empty Factory Tests

    [Fact]
    public void Empty_CreatesEmptySuccessResult()
    {
        // Act
        var result = PagedResult<Order>.Empty();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public void Empty_WithCustomPaging_UsesProvidedValues()
    {
        // Act
        var result = PagedResult<Order>.Empty(page: 5, pageSize: 50);

        // Assert
        result.Page.Should().Be(5);
        result.PageSize.Should().Be(50);
    }

    #endregion

    #region Skip Calculation Tests

    [Theory]
    [InlineData(1, 10, 0)]   // Page 1, size 10 = skip 0
    [InlineData(2, 10, 10)]  // Page 2, size 10 = skip 10
    [InlineData(3, 20, 40)]  // Page 3, size 20 = skip 40
    [InlineData(1, 25, 0)]   // Page 1, size 25 = skip 0
    public void Skip_CalculatesCorrectly(int page, int pageSize, int expectedSkip)
    {
        // Act
        var result = PagedResult<Order>.Success([], totalCount: 100, page, pageSize);

        // Assert
        result.Skip.Should().Be(expectedSkip);
    }

    #endregion
}
