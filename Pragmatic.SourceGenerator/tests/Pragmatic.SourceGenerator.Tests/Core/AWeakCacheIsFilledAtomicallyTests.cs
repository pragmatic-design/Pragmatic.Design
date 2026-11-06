using System.IO;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     A <c>ConditionalWeakTable</c> in the generator is filled with <c>GetValue</c>, never with <c>TryGetValue</c> then <c>Add</c> (#566).
/// </summary>
/// <remarks>
///     <para>
///         The generator's transforms run in parallel. Two that ask the same memoised question about the same assembly both miss, both
///         compute, and the second <c>Add</c> throws <c>ArgumentException: An item with the same key has already been added</c>. The
///         exception escapes the transform as CS8785, and the whole generator's output is lost. Seen once in four builds of one tree, in
///         <c>BoundaryOwnershipReader.SingleBoundaryOf</c>, called from <c>EntityTransform</c>.
///     </para>
///     <para>
///         <c>GetValue(key, factory)</c> is atomic: a factory that loses the race has its value dropped, and both callers get the one
///         stored. The interleaving cannot be held open from a test, because the window is inside the BCL. So what is pinned is the
///         shape: no table of the generator's sources is ever <c>Add</c>ed to.
///     </para>
/// </remarks>
public class AWeakCacheIsFilledAtomicallyTests
{
    [Fact]
    public void NoWeakTableOfTheGenerator_IsFilledWithAdd()
    {
        var offenders = GeneratorSources().SelectMany(f => AddsToAWeakTable(File.ReadAllText(f)).Select(a => $"{Path.GetFileName(f)}: {a}")).ToList();

        offenders.Should().BeEmpty("a TryGetValue then Add races between parallel transforms; fill the table with GetValue(key, factory)");
    }

    /// <summary>The control: the rule finds the shape it forbids, so an empty answer above means none, not a rule that sees nothing.</summary>
    [Fact]
    public void TheRule_FindsATryGetValueThenAdd()
    {
        const string racy = """
            class C
            {
                private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> Cache = new();
                static object Of(object key)
                {
                    if (Cache.TryGetValue(key, out var hit)) return hit;
                    var made = new object();
                    Cache.Add(key, made);
                    return made;
                }
            }
            """;

        AddsToAWeakTable(racy).Should().Equal(["Cache.Add(key, made)"]);
    }

    /// <summary>Every <c>T.Add(…)</c> where <c>T</c> is a field declared as a <c>ConditionalWeakTable</c> in the same source.</summary>
    private static List<string> AddsToAWeakTable(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var tables = root.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Where(f => f.Declaration.Type.ToString().Contains("ConditionalWeakTable", StringComparison.Ordinal))
            .SelectMany(f => f.Declaration.Variables.Select(v => v.Identifier.ValueText))
            .ToHashSet(StringComparer.Ordinal);

        return root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Add", Expression: IdentifierNameSyntax target } && tables.Contains(target.Identifier.ValueText))
            .Select(i => i.ToString())
            .ToList();
    }

    /// <summary>The generator's own sources and the shared code linked into it, read from the repository.</summary>
    private static IEnumerable<string> GeneratorSources()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pragmatic.Design.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        var sources = new[] { Path.Combine(root.FullName, "Pragmatic.SourceGenerator", "src"), Path.Combine(root.FullName, "shared", "SourceGen") }
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        sources.Should().Contain(f => f.EndsWith("BoundaryOwnershipReader.cs", StringComparison.Ordinal), "the scan reaches the file the crash came from");
        return sources;
    }
}
