using System.Text.Json;
using Pragmatic.Persistence.Query.Results;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.Tests.QueryTests;

/// <summary>
///     The JSON shape of a page: the page's members and not the outcome, which the status code carries
///     (#939). The cache reads the same JSON back, so a round trip must still know a success.
/// </summary>
public class PagedResultJsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Serialize_ASuccess_WritesNoOutcome()
    {
        var page = PagedResult<string>.Success(["a", "b"], totalCount: 3, page: 1, pageSize: 2);

        var members = JsonDocument.Parse(JsonSerializer.Serialize(page, Web)).RootElement
            .EnumerateObject().Select(p => p.Name).ToList();

        members.Should().NotContain("isSuccess");
        members.Should().NotContain("isFailure");
        members.Should().Contain("items");
    }

    [Fact]
    public void RoundTrip_ASuccess_IsStillASuccess()
    {
        var page = PagedResult<string>.Success(["a", "b"], totalCount: 3, page: 1, pageSize: 2);

        var back = JsonSerializer.Deserialize<PagedResult<string>>(JsonSerializer.Serialize(page, Web), Web)!;

        back.IsSuccess.Should().BeTrue();
        back.Items.Should().BeEquivalentTo(["a", "b"]);
        back.TotalCount.Should().Be(3);
        back.HasNextPage.Should().BeTrue();
    }
}
