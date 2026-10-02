using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Endpoints.Samples.Errors;
using Pragmatic.Endpoints.Samples.Models;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Creates a new user.
/// </summary>
[Endpoint(HttpVerb.Post, "/users")]
[ApiSummary("Create User")]
[ApiDescription("Creates a new user with the provided information.")]
[ApiTags("Users")]
[HttpStatus(201)]
public partial class CreateUserEndpoint : Endpoint<UserResponse, ValidationError>
{
    /// <summary>
    ///     The user's name.
    /// </summary>
    [FromBody]
    public required string Name { get; init; }

    /// <summary>
    ///     The user's email address.
    /// </summary>
    [FromBody]
    public required string Email { get; init; }

    /// <inheritdoc />
    public override Task<Result<UserResponse, ValidationError>> HandleAsync(CancellationToken ct = default)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(Name))
        {
            var error = new ValidationError
            {
                Field = nameof(Name),
                Message = "Name is required and cannot be empty."
            };
            return Task.FromResult(Result<UserResponse, ValidationError>.Failure(error));
        }

        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@'))
        {
            var error = new ValidationError
            {
                Field = nameof(Email),
                Message = "A valid email address is required."
            };
            return Task.FromResult(Result<UserResponse, ValidationError>.Failure(error));
        }

        // Create the user
        var response = new UserResponse
        {
            Id = Guid.NewGuid(),
            Name = Name,
            Email = Email,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return Task.FromResult(Result<UserResponse, ValidationError>.Success(response));
    }
}