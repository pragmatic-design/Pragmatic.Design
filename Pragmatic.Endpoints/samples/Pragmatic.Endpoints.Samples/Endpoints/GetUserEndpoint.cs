using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Endpoints.Samples.Errors;
using Pragmatic.Endpoints.Samples.Models;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Gets a user by their ID.
/// </summary>
[Endpoint(HttpVerb.Get, "/users/{id}")]
[ApiSummary("Get User")]
[ApiDescription("Retrieves a user by their unique identifier.")]
[ApiTags("Users")]
public partial class GetUserEndpoint : Endpoint<UserResponse, NotFoundError>
{
    /// <summary>
    ///     The user's unique identifier.
    /// </summary>
    [FromRoute]
    public Guid Id { get; set; }

    /// <inheritdoc />
    public override Task<Result<UserResponse, NotFoundError>> HandleAsync(CancellationToken ct = default)
    {
        // Simulate user lookup - in reality this would query a database
        if (Id == Guid.Empty)
        {
            var error = new NotFoundError
            {
                ResourceType = "User",
                ResourceId = Id.ToString()
            };
            return Task.FromResult(Result<UserResponse, NotFoundError>.Failure(error));
        }

        var response = new UserResponse
        {
            Id = Id,
            Name = "John Doe",
            Email = "john.doe@example.com",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        return Task.FromResult(Result<UserResponse, NotFoundError>.Success(response));
    }
}