using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     A specification a computed body passes to a query that takes a value from the row (PRAG0735).
/// </summary>
/// <param name="Argument">The argument as written — <c>LineSpecifications.AtLeast(Threshold)</c>.</param>
/// <param name="Location">Where it is written.</param>
internal sealed record SpecificationReadingTheRowModel(string Argument, LocationInfo? Location);
