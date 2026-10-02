using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Pragmatic.Endpoints.Serialization;

/// <summary>
///     Source-generated JSON metadata for the error shape every Pragmatic endpoint can return.
/// </summary>
/// <remarks>
///     <para>
///         It cannot live in <c>PragmaticCommonJsonContext</c>: that one is in
///         <c>Pragmatic.Abstractions</c>, which does not reference ASP.NET, and
///         <see cref="ProblemDetails" /> comes from ASP.NET.
///     </para>
///     <para>
///         Without it, an AOT publish with the reflection fallback disabled fails on the error path —
///         the one that runs precisely when something has already gone wrong. The extension bag is
///         <c>IDictionary&lt;string, object?&gt;</c>, so the shapes Pragmatic puts in it are declared
///         here too; anything else a caller adds still needs its own metadata.
///     </para>
///     <para>
///         <c>HttpValidationProblemDetails</c> is deliberately absent: the source generator refuses it
///         (SYSLIB1030) because its <c>Errors</c> dictionary has no supported metadata shape. The
///         validation payload Pragmatic produces goes through <c>ProblemDetails.Extensions</c>
///         instead, which is covered.
///     </para>
/// </remarks>
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSerializable(typeof(string[]))]
// Matching what the host configures for HTTP JSON, so the fallback below produces the same bytes
// as the seam would.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class PragmaticProblemDetailsJsonContext : JsonSerializerContext;
