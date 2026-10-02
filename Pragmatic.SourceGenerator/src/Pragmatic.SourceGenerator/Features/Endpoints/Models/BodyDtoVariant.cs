using System.Collections.Immutable;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     One request-body record the generator emits for an endpoint: its name, the properties it carries,
///     and the API version it belongs to when the endpoint is versioned.
/// </summary>
/// <remarks>
///     An unversioned endpoint has exactly one; a versioned one has a variant per version, each with the
///     properties available then. The name is stated once here because three places need it — the two
///     templates that emit the records, and the AOT JSON context that has to cover them. It was derived
///     independently the first time, and the copy that guessed <c>{Type}Body</c> for a versioned endpoint
///     put a type that does not exist into the generated context.
/// </remarks>
/// <param name="Name">The record's simple name.</param>
/// <param name="Properties">The properties it declares.</param>
/// <param name="ApiVersion">The API version it serves, or <c>null</c> when the endpoint is unversioned.</param>
internal sealed record BodyDtoVariant(
    string Name,
    ImmutableArray<BodyPropertyModel> Properties,
    string? ApiVersion);
