using Pragmatic.Mapping.EFCore.Extensions;
using Pragmatic.Mapping.EFCore.Tests.Dtos;

namespace Pragmatic.Mapping.EFCore.Tests;

/// <summary>
///     Integration tests for the paginated projection helpers (ToPagedDtoAsync / ToSliceDtoAsync).
///     Three users are seeded by <see cref="PostgresTestBase" />.
/// </summary>
public class PaginationTests : PostgresTestBase
{
    [Fact]
    public async Task ToPagedDtoAsync_FirstPage_ReturnsItemsAndMetadata()
    {
        var page = await Db.Users
            .OrderBy(u => u.Id)
            .ToPagedDtoAsync(UserDto.Projection, pageNumber: 1, pageSize: 2);

        page.Items.Should().HaveCount(2);
        page.TotalCount.Should().Be(3);
        page.Number.Should().Be(1);
        page.PageSize.Should().Be(2);
        page.TotalPages.Should().Be(2);
        page.HasNextPage.Should().BeTrue();
        page.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task ToPagedDtoAsync_LastPage_ReturnsRemainder()
    {
        var page = await Db.Users
            .OrderBy(u => u.Id)
            .ToPagedDtoAsync(UserDto.Projection, pageNumber: 2, pageSize: 2);

        page.Items.Should().HaveCount(1);
        page.TotalCount.Should().Be(3);
        page.HasNextPage.Should().BeFalse();
        page.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task ToPagedDtoAsync_OrderedPages_DoNotOverlap()
    {
        var p1 = await Db.Users.OrderBy(u => u.Id)
            .ToPagedDtoAsync(UserDto.Projection, pageNumber: 1, pageSize: 2);
        var p2 = await Db.Users.OrderBy(u => u.Id)
            .ToPagedDtoAsync(UserDto.Projection, pageNumber: 2, pageSize: 2);

        var ids = p1.Items.Select(i => i.Id).Concat(p2.Items.Select(i => i.Id)).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().HaveCount(3); // every row appears exactly once across the two pages
    }

    [Fact]
    public async Task ToPagedDtoAsync_ProjectsThroughToDto()
    {
        var page = await Db.Users
            .Where(u => u.Email == "john.doe@example.com")
            .OrderBy(u => u.Id)
            .ToPagedDtoAsync(UserDto.Projection, pageNumber: 1, pageSize: 10);

        page.Items.Should().ContainSingle()
            .Which.AddressCity.Should().Be("New York"); // flattened projection ran
    }

    [Fact]
    public async Task ToSliceDtoAsync_OffsetLimit_ReturnsSlice()
    {
        var slice = await Db.Users
            .OrderBy(u => u.Id)
            .ToSliceDtoAsync(UserDto.Projection, offset: 1, limit: 1);

        slice.Should().HaveCount(1);
    }

    [Fact]
    public async Task ToPagedDtoAsync_PageNumberBelowOne_Throws()
    {
        var ex = await Record.ExceptionAsync(() => Db.Users
            .OrderBy(u => u.Id)
            .ToPagedDtoAsync(UserDto.Projection, pageNumber: 0, pageSize: 10));

        ex.Should().BeAssignableTo<ArgumentException>();
    }

    [Fact]
    public async Task ToSliceDtoAsync_NegativeOffset_Throws()
    {
        var ex = await Record.ExceptionAsync(() => Db.Users
            .OrderBy(u => u.Id)
            .ToSliceDtoAsync(UserDto.Projection, offset: -1, limit: 10));

        ex.Should().BeAssignableTo<ArgumentException>();
    }
}
