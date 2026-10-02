using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Temporal.Models;

/// <summary>One property whose type is one of the eight temporal types.</summary>
/// <param name="Name">The property name, as EF will map it.</param>
/// <param name="TypeName">Its CLR type, fully qualified, nullable wrapper included.</param>
internal sealed record TemporalPropertyEntryModel(string Name, string TypeName);

/// <summary>A type that declares at least one temporal property.</summary>
/// <param name="Namespace">The declaring namespace, empty for the global one.</param>
/// <param name="TypeName">The simple name.</param>
/// <param name="UniqueName">The name including outer types, joined with '_'.</param>
/// <param name="FullyQualifiedName">The name the generated code writes, with global::.</param>
/// <param name="Properties">Its temporal properties, in declaration order.</param>
internal sealed record TemporalEntityModel(
    string Namespace,
    string TypeName,
    string UniqueName,
    string FullyQualifiedName,
    EquatableArray<TemporalPropertyEntryModel> Properties);
