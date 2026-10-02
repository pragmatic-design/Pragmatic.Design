using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Endpoints.Samples.Endpoints.Groups;
using Pragmatic.Endpoints.Samples.Models;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Lists all users with optional pagination.
/// </summary>
[Endpoint(HttpVerb.Get, "/")]
[EndpointGroup<UsersGroup>]
[ApiSummary("List Users")]
[ApiDescription("Returns a paginated list of all users.")]
public partial class ListUsersEndpoint : Endpoint<UserResponse[]>
{
    /// <summary>
    ///     Page number (1-based).
    /// </summary>
    [FromQuery(Name = "page")]
    public int Page { get; set; } = 1;

    /// <summary>
    ///     Number of items per page.
    /// </summary>
    [FromQuery(Name = "pageSize")]
    public int PageSize { get; set; } = 10;

    /// <inheritdoc />
    public override Task<Result<UserResponse[]>> HandleAsync(CancellationToken ct = default)
    {
        // Simulate user list - in reality this would query a database
        var users = new[]
        {
            new UserResponse
            {
                Id = Guid.NewGuid(),
                Name = "John Doe",
                Email = "john.doe@example.com",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
            },
            new UserResponse
            {
                Id = Guid.NewGuid(),
                Name = "Jane Smith",
                Email = "jane.smith@example.com",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-15)
            }
        };

        return Task.FromResult(Result<UserResponse[]>.Success(users));
    }
}