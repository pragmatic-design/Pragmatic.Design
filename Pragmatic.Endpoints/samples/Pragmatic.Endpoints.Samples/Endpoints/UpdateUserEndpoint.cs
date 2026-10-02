using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Endpoints.Samples.Errors;
using Pragmatic.Endpoints.Samples.Models;
using Pragmatic.Endpoints.Samples.Services;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Updates an existing user.
///     Demonstrates all binding scenarios for raw endpoints:
///     - [FromRoute] for route parameters
///     - [FromHeader] for header parameters
///     - [FromBody] for body properties
///     - Private field for DI (IUserRepository)
/// </summary>
[Endpoint(HttpVerb.Put, "/users/{id}")]
[ApiSummary("Update User")]
[ApiDescription("Updates an existing user with the provided information.")]
[ApiTags("Users")]
public partial class UpdateUserEndpoint : Endpoint<UserResponse, NotFoundError, ValidationError>
{
    // DI dependency - private field with service type (not readonly for SetDependencies)
    private IUserRepository _repository = null!;

    /// <summary>
    ///     The user's unique identifier.
    /// </summary>
    [FromRoute]
    public Guid Id { get; set; }

    /// <summary>
    ///     Correlation ID for request tracing.
    /// </summary>
    [FromHeader(Name = "X-Correlation-Id")]
    public string? CorrelationId { get; set; }

    /// <summary>
    ///     Required API key for authentication.
    /// </summary>
    [FromHeader(Name = "X-Api-Key")]
    public required string ApiKey { get; set; }

    /// <summary>
    ///     The user's updated name.
    /// </summary>
    [FromBody]
    public required string Name { get; init; }

    /// <summary>
    ///     The user's updated email address.
    /// </summary>
    [FromBody]
    public required string Email { get; init; }

    /// <inheritdoc />
    public override async Task<Result<UserResponse, NotFoundError, ValidationError>> HandleAsync(
        CancellationToken ct = default)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(Name))
            return new ValidationError
            {
                Field = nameof(Name),
                Message = "Name is required and cannot be empty."
            };

        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@'))
            return new ValidationError
            {
                Field = nameof(Email),
                Message = "A valid email address is required."
            };

        // Update user via repository
        var updated = await _repository.UpdateAsync(Id, Name, Email, ct);

        if (updated is null)
            return new NotFoundError
            {
                ResourceType = "User",
                ResourceId = Id.ToString()
            };

        return new UserResponse
        {
            Id = updated.Id,
            Name = updated.Name,
            Email = updated.Email,
            CreatedAt = updated.CreatedAt
        };
    }
}