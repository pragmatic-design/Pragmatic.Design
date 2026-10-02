using Pragmatic.Testing.Assertions;
using Pragmatic.Pagination;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     <see cref="Page{T}" />: a plain page of items. It was <c>PagedResult&lt;T&gt;</c>, the name of the
///     Persistence result of a paged query in another namespace, and a file importing both got CS0104.
///     The page number is <c>Number</c>: a member cannot be named after its type.
/// </summary>
public sealed class PageTests
{
    [Fact]
    public void Ctor_ValidArguments_ExposesMetadata()
    {
        var page = new Page<int>([1, 2, 3], totalCount: 10, number: 2, pageSize: 3);

        page.Items.Should().Equal(1, 2, 3);
        page.TotalCount.Should().Be(10);
        page.Number.Should().Be(2);
        page.PageSize.Should().Be(3);
    }

    [Theory]
    [InlineData(0, 10, 0, "number")]       // number < 1
    [InlineData(1, -1, 0, "pageSize")]     // pageSize < 0
    [InlineData(1, 10, -1, "totalCount")]  // totalCount < 0
    public void Ctor_InvalidArguments_Throws(int number, int pageSize, int totalCount, string paramName)
    {
        var act = () => new Page<int>([], totalCount, number, pageSize);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(paramName);
    }

    [Fact]
    public void Ctor_NullItems_ThrowsInsteadOfDeferringToFirstAccess()
    {
        var act = () => new Page<int>(null!, totalCount: 0, number: 1, pageSize: 10);
        act.Should().Throw<ArgumentNullException>().WithParameterName("items");
    }

    [Theory]
    [InlineData(10, 3, 4)]   // ceiling(10/3)
    [InlineData(9, 3, 3)]    // exact division
    [InlineData(0, 3, 0)]    // empty set
    public void TotalPages_UsesCeiling(int totalCount, int pageSize, int expected)
        => new Page<int>([], totalCount, 1, pageSize).TotalPages.Should().Be(expected);

    [Fact]
    public void TotalPages_PageSizeZero_IsZeroInsteadOfDividingByZero()
        => new Page<int>([], totalCount: 5, number: 1, pageSize: 0).TotalPages.Should().Be(0);

    [Theory]
    [InlineData(1, false, true)]   // first page: no previous, has next
    [InlineData(2, true, true)]    // middle page
    [InlineData(4, true, false)]   // last page (10 items / 3 per page = 4 pages)
    public void HasPreviousNext_ReflectPosition(int number, bool hasPrevious, bool hasNext)
    {
        var page = new Page<int>([], totalCount: 10, number, pageSize: 3);

        page.HasPreviousPage.Should().Be(hasPrevious);
        page.HasNextPage.Should().Be(hasNext);
    }

    /// <summary>
    ///     One <c>PagedResult&lt;T&gt;</c> in the ecosystem, the Persistence result: Abstractions declares no
    ///     type of that name.
    /// </summary>
    [Fact]
    public void Abstractions_DeclaresNoPagedResult()
        => typeof(Page<>).Assembly.GetTypes().Should().NotContain(t => t.Name == "PagedResult`1");
}
