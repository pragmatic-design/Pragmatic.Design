using Pragmatic.Actions.Abstractions;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Samples.Errors;
using Pragmatic.Endpoints.Samples.Services;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Deletes a user using the VoidDomainAction pattern.
///     Demonstrates VoidDomainAction endpoint (returns 204 No Content on success).
/// </summary>
[Endpoint(HttpVerb.Delete, "/users/{id}")]
[ApiSummary("Delete User")]
[ApiDescription("Deletes a user by their unique identifier.")]
[ApiTags("Users")]
public partial class DeleteUserEndpoint : VoidDomainAction<NotFoundError>
{
    // DI dependency - injected by DomainActionInvoker
    private readonly IUserRepository _repository = null!;

    /// <summary>
    ///     The user's unique identifier.
    /// </summary>
    [FromRoute]
    public Guid Id { get; set; }

    /// <summary>
    ///     Whether to soft delete.
    /// </summary>
    [FromQuery]
    public bool SoftDelete { get; set; }

    /// <inheritdoc />
    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var deleted = await _repository.DeleteAsync(Id, ct);

        if (!deleted)
            return new NotFoundError
            {
                ResourceType = "User",
                ResourceId = Id.ToString()
            };

        return Success;
    }
}