using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;

namespace Pragmatic.Abstractions.CodeFixers;

/// <summary>
///     Quick-fix for PRAG2800: adds <c>[JsonSerializable(typeof(Payload))]</c> to the project's
///     <c>JsonSerializerContext</c> so the flagged message / event / job payload becomes AOT-safe.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddJsonSerializableCodeFixProvider))]
[Shared]
public sealed class AddJsonSerializableCodeFixProvider : CodeFixProvider
{
    private const string JsonSerializerContextMetadataName = "System.Text.Json.Serialization.JsonSerializerContext";
    private const string JsonSerializableAttributeName = "global::System.Text.Json.Serialization.JsonSerializableAttribute";

    private static readonly string[] MarkerMetadataNames =
    [
        "Pragmatic.Messaging.IMessageHandler`1",
        "Pragmatic.Events.IDomainEventHandler`1",
        "Pragmatic.Jobs.IJob`1",
    ];

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("PRAG2800");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
            return;

        var diagnostic = context.Diagnostics[0];
        var classNode = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classNode is null)
            return;

        if (model.GetDeclaredSymbol(classNode, context.CancellationToken) is not INamedTypeSymbol handler)
            return;

        var payload = FindPayloadType(handler, model.Compilation);
        if (payload is null)
            return;

        var contextSymbol = FindUserJsonContext(model.Compilation);
        var contextRef = contextSymbol?.DeclaringSyntaxReferences.FirstOrDefault();
        if (contextRef is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Add [JsonSerializable(typeof({payload.Name}))] to {contextSymbol!.Name}",
                ct => AddAttributeAsync(context.Document.Project.Solution, contextRef, payload, ct),
                equivalenceKey: "PRAG2800.AddJsonSerializable"),
            diagnostic);
    }

    private static async Task<Solution> AddAttributeAsync(
        Solution solution, SyntaxReference contextRef, ITypeSymbol payload, CancellationToken ct)
    {
        var contextNode = await contextRef.GetSyntaxAsync(ct).ConfigureAwait(false);
        var document = solution.GetDocument(contextNode.SyntaxTree);
        if (document is null)
            return solution;

        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);
        var g = editor.Generator;

        var attribute = g.Attribute(
            JsonSerializableAttributeName,
            new[] { g.TypeOfExpression(g.TypeExpression(payload)) });

        editor.AddAttribute(contextNode, attribute);

        // Apply Simplifier (reduces the fully-qualified names) and Formatter, THEN normalize line
        // endings to the document's convention so the fix never leaves mixed CR/LF endings.
        var changed = editor.GetChangedDocument();
        changed = await Simplifier.ReduceAsync(changed, Simplifier.Annotation, cancellationToken: ct).ConfigureAwait(false);
        changed = await Formatter.FormatAsync(changed, cancellationToken: ct).ConfigureAwait(false);

        var originalText = await document.GetTextAsync(ct).ConfigureAwait(false);
        var newLine = DetectNewLine(originalText);
        var changedText = await changed.GetTextAsync(ct).ConfigureAwait(false);
        var normalized = changedText.ToString().Replace("\r\n", "\n").Replace("\n", newLine);
        return changed.WithText(SourceText.From(normalized, originalText.Encoding)).Project.Solution;
    }

    private static string DetectNewLine(SourceText text)
    {
        foreach (var line in text.Lines)
        {
            var breakLength = line.SpanIncludingLineBreak.Length - line.Span.Length;
            if (breakLength > 0)
                return text.ToString(new TextSpan(line.Span.End, breakLength));
        }

        return "\n";
    }

    private static ITypeSymbol? FindPayloadType(INamedTypeSymbol handler, Compilation compilation)
    {
        var markers = MarkerMetadataNames
            .Select(compilation.GetTypeByMetadataName)
            .Where(s => s is not null)
            .ToArray();

        foreach (var iface in handler.AllInterfaces)
        {
            if (!iface.IsGenericType)
                continue;

            if (markers.Any(m => SymbolEqualityComparer.Default.Equals(m, iface.OriginalDefinition))
                && iface.TypeArguments.Length == 1)
            {
                return iface.TypeArguments[0];
            }
        }

        return null;
    }

    private static INamedTypeSymbol? FindUserJsonContext(Compilation compilation)
    {
        var contextBase = compilation.GetTypeByMetadataName(JsonSerializerContextMetadataName);
        if (contextBase is null)
            return null;

        foreach (var type in EnumerateNamedTypes(compilation.Assembly.GlobalNamespace))
        {
            if (!type.DeclaringSyntaxReferences.IsDefaultOrEmpty && InheritsFrom(type, contextBase))
                return type;
        }

        return null;
    }

    /// <summary>
    ///     Every named type in the assembly, including types nested inside other types — the analyzer
    ///     walks the same way, and the fixer has to see the same contexts it does or it cannot offer
    ///     the one the user actually declared.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol childNs)
            {
                foreach (var nested in EnumerateNamedTypes(childNs))
                    yield return nested;
            }
            else if (member is INamedTypeSymbol type)
            {
                foreach (var nested in EnumerateWithNested(type))
                    yield return nested;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateWithNested(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var member in type.GetTypeMembers())
        {
            foreach (var nested in EnumerateWithNested(member))
                yield return nested;
        }
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        return false;
    }
}
