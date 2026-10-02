using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Pragmatic.SourceGenerator.Features.Serialization.Analysis;

/// <summary>
///     Resolves a written type expression — <c>string</c>, <c>System.Guid?</c>,
///     <c>global::Ns.Status</c>, <c>global::System.Collections.Generic.List&lt;int&gt;</c> — back to the
///     symbol it names.
/// </summary>
/// <remarks>
///     <para>
///         Needed because some endpoint models are assembled by the generator rather than read from
///         source: the Resource CRUD endpoints and the trait endpoints describe their request bodies as
///         type <em>text</em>, since a value-equatable model cannot hold symbols. Their bodies are real
///         HTTP surface all the same, and the AOT context has to cover them.
///     </para>
///     <para>
///         Binding is speculative against a tree already in the compilation, so the whole C# type
///         grammar works — generics, arrays, nullable — without a hand-written parser that would
///         understand only the cases someone thought of.
///     </para>
/// </remarks>
internal sealed class JsonTypeExpressionResolver
{
    private readonly SemanticModel? _model;
    private readonly Dictionary<string, ITypeSymbol?> _cache = new(System.StringComparer.Ordinal);

    public JsonTypeExpressionResolver(Compilation compilation)
    {
        var tree = compilation.SyntaxTrees.FirstOrDefault();
        _model = tree is null ? null : compilation.GetSemanticModel(tree);
    }

    /// <summary>The type the expression names, or <c>null</c> when it does not bind.</summary>
    public ITypeSymbol? Resolve(string typeExpression)
    {
        if (_model is null || string.IsNullOrWhiteSpace(typeExpression))
            return null;

        if (_cache.TryGetValue(typeExpression, out var cached))
            return cached;

        var syntax = SyntaxFactory.ParseTypeName(typeExpression);
        var resolved = syntax.ContainsDiagnostics
            ? null
            : _model.GetSpeculativeTypeInfo(0, syntax, SpeculativeBindingOption.BindAsTypeOrNamespace).Type;

        // Position 0 is the top of the file, so only a fully-qualified or keyword name binds — which is
        // exactly what the model builders write. An unresolved name is an ErrorTypeSymbol, not null.
        if (resolved is null or IErrorTypeSymbol)
            resolved = null;

        _cache[typeExpression] = resolved;
        return resolved;
    }
}
