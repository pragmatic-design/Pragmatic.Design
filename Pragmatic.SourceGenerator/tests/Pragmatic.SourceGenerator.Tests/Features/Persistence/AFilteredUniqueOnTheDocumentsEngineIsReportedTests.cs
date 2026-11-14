using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>[Unique(Filter = …)]</c> on the documents engine (#729) is <c>PRAG3113</c>: a document index carries no SQL predicate, so the
///     index is unique over every document, stricter than declared, and the generator says so rather than leaving it to a refused write.
/// </summary>
public sealed class AFilteredUniqueOnTheDocumentsEngineIsReportedTests
{
    private const string DocumentsBridge = """
        namespace Pragmatic.Persistence.Documents
        {
            public static class PragmaticDocFilters { }
            public sealed class DocCompiledSpecification<T> { }
            public sealed class DocUnitOfWork { }
        }
        """;

    private const string Entities = """
        using Pragmatic.Persistence.Entity;

        namespace Probe.Desks
        {
            [Entity]
            [Unique(nameof(Desk), Filter = "\"ValidTo\" IS NULL")]
            public partial class Stretch : IEntity
            {
                public string Desk { get; set; } = "";
                public System.DateTimeOffset? ValidTo { get; set; }
            }

            [Entity]
            [Unique(nameof(Code))]
            public partial class Voucher : IEntity { public string Code { get; set; } = ""; }
        }

        """;

    [Fact]
    public void OnTheDocumentsEngine_AFilteredUnique_IsPrag3113()
    {
        var result = GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(Entities + DocumentsBridge,
            "Pragmatic.Persistence.Documents.Generated", References);

        var reported = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3113").ToList();
        reported.Count.Should().Be(1, "the control: Voucher's [Unique] declares no filter");
        reported[0].GetMessage().Should().Contain("[Unique] on Stretch declares the filter '\"ValidTo\" IS NULL'");
        reported[0].Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>(),
        .. new[] { "Pragmatic.Specification", "Pragmatic.Abstractions", "Pragmatic.Persistence.Expressions", "System.Linq.Queryable", "System.Linq.Expressions", "System.ComponentModel.Annotations" }
            .Select(n => GeneratorTestHelper.TryGetAssemblyReference(n) ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."))
    ];
}
