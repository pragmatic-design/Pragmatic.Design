namespace Pragmatic.SourceGenerator.Features.Specification.Models;

/// <summary>
///     One parameter of a declared specification, carried through to the generated extensions.
/// </summary>
/// <param name="TypeName">The parameter's type, fully qualified.</param>
/// <param name="Name">The parameter's name, kept as the author wrote it so the call reads the same.</param>
internal sealed record SpecificationParameter(string TypeName, string Name);
