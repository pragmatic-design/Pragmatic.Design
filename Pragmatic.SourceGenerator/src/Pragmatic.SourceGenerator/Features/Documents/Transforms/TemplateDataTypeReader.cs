using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Documents.Models;

namespace Pragmatic.SourceGenerator.Features.Documents.Transforms;

/// <summary>
///     Finds the types a template is given with their type known at compile time, and the types reached
///     from them, so their accessors can be written instead of read by reflection.
/// </summary>
/// <remarks>
///     <para>
///         The entry points are the type parameters marked <c>[TemplateData]</c> in
///         Pragmatic.Documents.Templating. The attribute decides; the names below only keep the semantic
///         model away from every other invocation in the compilation, and a test fails when the runtime
///         marks a member whose name is not here.
///     </para>
///     <para>
///         Reached means through a property, or through the elements of a collection property, which is
///         how a table's rows get theirs. A type is written only when the generated code can name it and
///         the lookup can find it: concrete, not anonymous, not generic over an open parameter, accessible
///         from this assembly, and not a framework type.
///     </para>
/// </remarks>
internal static class TemplateDataTypeReader
{
    private const string MarkerAttribute = "Pragmatic.Documents.Templating.Data.TemplateDataAttribute";

    /// <summary>The members whose type parameters the runtime marks. Kept in step by a test.</summary>
    internal static readonly ImmutableHashSet<string> EntryPointNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "AddSource", "Add", "AddAsync", "AddJsonFile", "AddJsonFileList",
        "StaticDataSource", "AsyncDataSource", "JsonFileDataSource", "JsonFileListDataSource");

    public static bool IsCandidate(SyntaxNode node) => node switch
    {
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } =>
            EntryPointNames.Contains(member.Name.Identifier.ValueText),
        InvocationExpressionSyntax { Expression: SimpleNameSyntax name } =>
            EntryPointNames.Contains(name.Identifier.ValueText),
        ObjectCreationExpressionSyntax { Type: GenericNameSyntax generic } =>
            EntryPointNames.Contains(generic.Identifier.ValueText),
        ObjectCreationExpressionSyntax { Type: QualifiedNameSyntax { Right: GenericNameSyntax generic } } =>
            EntryPointNames.Contains(generic.Identifier.ValueText),
        _ => false
    };

    public static EquatableArray<TemplateDataTypeModel> Read(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var compilation = context.SemanticModel.Compilation;
        var roots = new List<ITypeSymbol>();

        switch (context.Node)
        {
            case InvocationExpressionSyntax invocation
                when context.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol method:
                AddMarked(method.TypeParameters, method.TypeArguments, roots);
                break;

            case ObjectCreationExpressionSyntax creation
                when context.SemanticModel.GetTypeInfo(creation, ct).Type is INamedTypeSymbol created:
                AddMarked(created.TypeParameters, created.TypeArguments, roots);
                break;
        }

        if (roots.Count == 0)
            return EquatableArray<TemplateDataTypeModel>.Empty;

        var walk = new Walk(compilation);
        foreach (var root in roots)
            walk.Visit(root);

        return walk.Models.ToImmutableArray();
    }

    private static void AddMarked(
        ImmutableArray<ITypeParameterSymbol> parameters, ImmutableArray<ITypeSymbol> arguments, List<ITypeSymbol> roots)
    {
        for (var i = 0; i < parameters.Length && i < arguments.Length; i++)
        {
            if (parameters[i].GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == MarkerAttribute))
                roots.Add(arguments[i]);
        }
    }

    private sealed class Walk(Compilation compilation)
    {
        private readonly HashSet<ITypeSymbol> _seen = new(SymbolEqualityComparer.Default);

        public List<TemplateDataTypeModel> Models { get; } = [];

        public void Visit(ITypeSymbol type)
        {
            type = Unwrap(type);

            if (!_seen.Add(type))
                return;

            var elements = CollectionTypeHelper.GetElementTypes(type);
            if (elements.Count > 0)
            {
                foreach (var element in elements)
                    Visit(element);

                return;
            }

            if (type is not INamedTypeSymbol named || !CanBeWritten(named))
                return;

            var properties = ReadableProperties(named);
            if (properties is null)
                return;

            Models.Add(new TemplateDataTypeModel(
                named.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                properties.Select(p => p.Name).ToImmutableArray()));

            foreach (var property in properties)
                Visit(property.Type);
        }

        private static ITypeSymbol Unwrap(ITypeSymbol type)
            => type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
               && type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable
                ? nullable.TypeArguments[0]
                : type;

        private bool CanBeWritten(INamedTypeSymbol type)
        {
            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                return false;
            if (type.IsAbstract || type.IsAnonymousType || type.IsTupleType || type.IsRefLikeType)
                return false;
            if (type.SpecialType != SpecialType.None || IsFrameworkType(type))
                return false;
            if (ContainsTypeParameter(type))
                return false;

            for (var current = type; current is not null; current = current.ContainingType)
            {
                if (current.IsFileLocal)
                    return false;
            }

            return compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);
        }

        /// <summary>
        ///     The properties a template can name, or null when two of them differ only by case: the
        ///     lookup ignores case, as reflection does, and could not tell them apart.
        /// </summary>
        private List<IPropertySymbol>? ReadableProperties(INamedTypeSymbol type)
        {
            var properties = new List<IPropertySymbol>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object;
                 current = current.BaseType)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member is not IPropertySymbol { IsStatic: false, IsIndexer: false, ReturnsByRef: false, ReturnsByRefReadonly: false } property)
                        continue;
                    if (property.DeclaredAccessibility != Accessibility.Public || property.GetMethod is null)
                        continue;
                    if (!compilation.IsSymbolAccessibleWithin(property.GetMethod, compilation.Assembly))
                        continue;
                    if (property.Type.IsRefLikeType || property.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
                        continue;

                    // A base property hidden by a derived one of the same name: the derived one is what
                    // the template reads.
                    if (!names.Add(property.Name))
                        continue;
                    if (!folded.Add(property.Name))
                        return null;

                    properties.Add(property);
                }
            }

            return properties;
        }

        private static bool IsFrameworkType(INamedTypeSymbol type)
        {
            var ns = type.ContainingNamespace;
            while (ns is { IsGlobalNamespace: false, ContainingNamespace.IsGlobalNamespace: false })
                ns = ns.ContainingNamespace;

            return ns?.Name is "System" or "Microsoft";
        }

        private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter)
                                      || (named.ContainingType is { } outer && ContainsTypeParameter(outer)),
            _ => false
        };
    }
}
