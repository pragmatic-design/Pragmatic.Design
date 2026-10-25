using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The operations of this compilation that load an entity with navigations (<c>[LoadEntity(Include = …)]</c>): what an
///     engine that compiles its reads has to compile a read for, or refuse. Shared by the Sql and documents engines.
/// </summary>
/// <remarks>
///     The paths are the action transform's (<see cref="IncludePaths.Declared" />), so the key the invoker names and the read the engine
///     compiles are one list read twice.
/// </remarks>
internal static class LoadIncludes
{
    private const string LoadEntity = "Pragmatic.Actions.Attributes.LoadEntityAttribute";

    /// <summary>One operation's load of <see cref="Entity" /> with <see cref="Includes" />; <see cref="Unsupported" /> says why an engine compiling reads by key does not compile it.</summary>
    internal sealed record Declaration(INamedTypeSymbol Operation, INamedTypeSymbol Entity, string Attribute, ImmutableArray<string> Includes, string? Unsupported, Location Location)
    {
        /// <summary>The key the invoker names the read by (<see cref="IncludePaths.Key" />).</summary>
        public string Key => IncludePaths.Key(Includes);
    }

    /// <summary>Every load with an <c>Include</c> declared in <paramref name="compilation" />'s own operations.</summary>
    public static List<Declaration> Read(Compilation compilation)
    {
        var declarations = new List<Declaration>();
        foreach (var operation in PragmaticNames.AllTypes(compilation.Assembly.GlobalNamespace).Where(t => t.DeclaringSyntaxReferences.Length > 0))
        {
            foreach (var attribute in operation.GetAttributes())
            {
                if (attribute.AttributeClass is not { } type || !type.OriginalDefinition.ToDisplayString().StartsWith(LoadEntity + "<", System.StringComparison.Ordinal)
                    || type.TypeArguments.FirstOrDefault() is not INamedTypeSymbol entity)
                    continue;
                var include = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Include").Value.Value as string;
                var includes = IncludePaths.Declared(entity, include);
                if (includes.IsEmpty)
                    continue;

                var unsupported = LoadSpecification.NameOf(attribute) is not null ? "it loads by a specification"
                    : attribute.NamedArguments.Any(a => a.Key == "By") ? "it loads by the logic key (By)"
                    : null;
                declarations.Add(new Declaration(operation, entity, "LoadEntity", includes, unsupported,
                    attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? operation.Locations.FirstOrDefault() ?? Location.None));
            }
        }

        return declarations;
    }
}
