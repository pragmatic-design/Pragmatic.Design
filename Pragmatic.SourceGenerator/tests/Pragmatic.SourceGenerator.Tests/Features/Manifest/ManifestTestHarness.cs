using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Tests.Features.Manifest;

/// <summary>
///     Shared plumbing for the manifest tests: a real <see cref="Compilation" /> (the transform resolves
///     response/error types through it) and an exact reader for the raw-string constants the manifest
///     templates emit.
/// </summary>
internal static class ManifestTestHarness
{
    private static MetadataReference[]? _references;

    public static Compilation Compile(string source)
        => CSharpCompilation.Create(
            "Showcase.Booking",
            [CSharpSyntaxTree.ParseText(source, path: "TestSource.cs")],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

    private static MetadataReference[] References => _references ??=
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        MetadataReference.CreateFromFile(Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
        MetadataReference.CreateFromFile(Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Collections.dll")),
    ];

    /// <summary>
    ///     Returns the value of a <c>const string</c> field, letting the C# lexer do the un-escaping.
    ///     Hand-rolled substring extraction would have to re-implement raw-string indentation stripping,
    ///     which is exactly the part that can silently corrupt the payload under test.
    /// </summary>
    public static string ConstantValue(string generatedSource, string fieldName)
    {
        var root = CSharpSyntaxTree.ParseText(generatedSource).GetRoot();
        var variable = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Identifier.Text == fieldName)
            ?? throw new InvalidOperationException($"No field '{fieldName}' in the generated source.");

        return variable.Initializer?.Value is LiteralExpressionSyntax literal
            ? literal.Token.ValueText
            : throw new InvalidOperationException($"Field '{fieldName}' is not a literal.");
    }

    /// <summary>
    ///     Returns the compact manifest JSON carried by the <c>[assembly: PragmaticMetadata(...)]</c>
    ///     attribute — the copy the host actually reads back through <c>MetadataReader</c>.
    /// </summary>
    public static string AssemblyAttributeJson(string generatedSource)
    {
        var root = CSharpSyntaxTree.ParseText(generatedSource).GetRoot();
        var argument = root.DescendantNodes()
            .OfType<AttributeSyntax>()
            .Where(a => a.Name.ToString().Contains("PragmaticMetadata"))
            .SelectMany(a => a.ArgumentList?.Arguments ?? default)
            .Select(a => a.Expression)
            .OfType<LiteralExpressionSyntax>()
            .LastOrDefault()
            ?? throw new InvalidOperationException("No PragmaticMetadata attribute literal found.");

        return argument.Token.ValueText;
    }

    public static IReadOnlyList<string> DuplicateKeys(System.Text.Json.JsonElement obj)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new List<string>();
        foreach (var property in obj.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                duplicates.Add(property.Name);
        }

        return duplicates;
    }
}
