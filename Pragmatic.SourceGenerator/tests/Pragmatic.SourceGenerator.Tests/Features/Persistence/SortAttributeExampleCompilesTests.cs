using System.Net;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The <c>&lt;example&gt;</c> in <c>SortAttribute</c>'s own documentation compiles.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The example and the property's type have to agree:
///         <c>DefaultDirection = SortDirection.Descending</c> against an <c>int</c> property is
///         <c>CS0266</c>. That example is the only documentation of the fixed-sort form and it is what
///         IntelliSense shows on hover, so a wrong one teaches, in the one place a reader looks, a line
///         that cannot be written.
///     </para>
///     <para>
///         The example is <b>read out of the file</b> rather than copied here. A copy is a second
///         source that agrees until somebody edits one of them, and the thing being asserted is
///         precisely that the documentation and the compiler agree.
///     </para>
/// </remarks>
public class SortAttributeExampleCompilesTests
{
    private const string AttributeFile =
        "Pragmatic.Persistence/src/Pragmatic.Persistence/Query/Attributes/SortAttribute.cs";

    /// <summary>The example, as written in the file, is valid C#.</summary>
    [Fact]
    public void TheDocumentedExample_Compiles()
    {
        var example = ExampleFromTheAttributeFile();

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Preamble + example, References);

        var errors = GeneratorTestHelper.GetCompilationErrors(result)
            .Where(e => e.Severity == DiagnosticSeverity.Error)
            .Select(e => e.ToString())
            .ToList();

        errors.Should().BeEmpty();
    }

    /// <summary>
    ///     The control: the harness reports an error when the example is wrong.
    /// </summary>
    /// <remarks>
    ///     The same example with the direction written as a bare number — the form that compiled while
    ///     the property was an <c>int</c>, and the form this change ends. Without this case, "the
    ///     example compiles" would also be satisfied by a harness that reports nothing at all, which is
    ///     how a compile-check quietly stops checking.
    /// </remarks>
    [Fact]
    public void TheHarness_ReportsAnExampleThatDoesNot()
    {
        var broken = ExampleFromTheAttributeFile()
            .Replace("DefaultDirection = SortDirection.Descending", "DefaultDirection = 1", StringComparison.Ordinal);

        broken.Should().NotBe(ExampleFromTheAttributeFile(),
            "the control has to differ from the case, or it is the same case twice");

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Preamble + broken, References);

        GeneratorTestHelper.GetCompilationErrors(result)
            .Should().Contain(e => e.Id == "CS0029" || e.Id == "CS0266");
    }

    /// <summary>
    ///     What the example leaves out: an entity, a DTO and the usings a query needs.
    /// </summary>
    private const string Preamble = """
        using System;
        using System.Linq;
        using System.Linq.Expressions;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace Docs.Sample;

        [Entity]
        public partial class Order : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string OrderNumber { get; set; } = "";
            public DateTimeOffset CreatedAt { get; set; }
        }

        public class OrderDto
        {
            public string OrderNumber { get; set; } = "";

            public static Expression<Func<Order, OrderDto>> Projection =>
                o => new OrderDto { OrderNumber = o.OrderNumber };
        }

        """;

    /// <summary>
    ///     The contents of the single <c>&lt;code&gt;</c> block inside the file's <c>&lt;example&gt;</c>,
    ///     stripped of its <c>///</c> prefixes and XML-unescaped.
    /// </summary>
    private static string ExampleFromTheAttributeFile()
    {
        var path = Path.Combine(RepositoryRoot(), AttributeFile.Replace('/', Path.DirectorySeparatorChar));

        File.Exists(path).Should().BeTrue(
            $"the example is read from '{AttributeFile}', and a test that cannot find it measures nothing");

        var text = File.ReadAllText(path);
        var block = Regex.Match(text, @"<code>(.*?)</code>", RegexOptions.Singleline);

        block.Success.Should().BeTrue("SortAttribute documents the fixed-sort form with a <code> example");

        var lines = block.Groups[1].Value
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Select(l => Regex.Replace(l, @"^\s*///\s?", ""))
            .Where(l => l.Length > 0 || true)
            .ToList();

        return WebUtility.HtmlDecode(string.Join("\n", lines));
    }

    /// <summary>
    ///     The repository root, found by walking up to the solution file.
    /// </summary>
    /// <remarks>
    ///     A test that reads a source file has to say where it looked when it does not find it — a
    ///     silent skip here would be a doc check that stopped checking without telling anybody.
    /// </remarks>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pragmatic.Design.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull(
            $"'Pragmatic.Design.slnx' must be found above '{AppContext.BaseDirectory}' for the example "
            + "to be read from the file it documents");

        return directory!.FullName;
    }

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<SortAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(MapFromAttribute<>)),
        GeneratorTestHelper.FromType<global::Pragmatic.Result.IError>()
    ];
}
