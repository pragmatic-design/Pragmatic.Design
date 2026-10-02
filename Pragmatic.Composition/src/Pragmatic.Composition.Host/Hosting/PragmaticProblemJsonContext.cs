using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     JSON metadata for the problem responses this host writes directly.
/// </summary>
/// <remarks>
///     A second, smaller context than the one in <c>Pragmatic.Endpoints.AspNetCore</c>, because this
///     assembly does not reference it and generated code may only name what its consumer already has.
///     Both cover <see cref="ProblemDetails" />; duplicating four lines of metadata is cheaper than a
///     package dependency added for them.
/// </remarks>
[JsonSerializable(typeof(ProblemDetails))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class PragmaticProblemJsonContext : JsonSerializerContext;
