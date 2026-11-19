using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     An <c>[Audited]</c> entity of a boundary the Sql or documents engine serves writes its trail through a save phase that the boundary's
///     generated <c>Add{Boundary}Persistence</c> registers only where its project references <c>Pragmatic.Audit.Engines</c> (#901). Without
///     the reference the host still registers the trail's reader, and every read answers an empty trail: PRAG3041 on the Sql engine, PRAG3114
///     on the documents engine, an error on the entity.
/// </summary>
/// <remarks>
///     The bridges are stubs of the types the generator recognises them by, as in <see cref="ALoadWithIncludeIsCompiledOrRefusedByItsEngineTests" />;
///     <c>Pragmatic.Audit.Engines</c> is recognised by its Sql entry row. The control with the row is also what shows the registration ran:
///     it calls the phase's registration.
/// </remarks>
public class AnAuditedEngineBoundaryWithoutItsTrailIsReportedTests
{
    private const string SqlBridge = """
        namespace Pragmatic.Persistence.Sql
        {
            public class SqlSession { }
            public static class PragmaticSqlFilters { }
            public class CompiledSpecification<T> { }
            public class SqlUnitOfWork { }
        }
        """;

    private const string DocumentsBridge = """
        namespace Pragmatic.Persistence.Documents
        {
            public static class PragmaticDocFilters { }
            public sealed class DocCompiledSpecification<T> { }
            public sealed class DocUnitOfWork { }
        }
        """;

    private const string AuditEngines = """
        namespace Pragmatic.Audit.Engines.Sql
        {
            public sealed class AuditEntryRow { }
        }
        """;

    private const string Entities = """
        using Pragmatic.Persistence.Entity;

        namespace Probe.Audit
        {
            [Pragmatic.Actions.Attributes.Boundary] public partial class LedgerBoundary;

            [Entity, Audited] public partial class Entry : IEntity { public string Label { get; set; } = ""; }

            [Entity] public partial class Note : IEntity { public string Text { get; set; } = ""; }
        }
        """;

    [Fact]
    public void OnTheSqlEngine_AnAuditedEntityWithoutAuditEngines_IsPrag3041()
    {
        var result = Run(Entities + SqlBridge);

        var reported = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3041").ToList();
        reported.Count.Should().Be(1, "Entry is [Audited]; Note is not");
        reported[0].Severity.Should().Be(DiagnosticSeverity.Error);
        reported[0].GetMessage().Should().Contain("Entry");
        reported[0].GetMessage().Should().Contain("Pragmatic.Audit.Engines");
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3114").Should().BeEmpty("the documents engine is not referenced");
    }

    [Fact]
    public void OnTheSqlEngine_WithAuditEngines_TheTrailIsRegisteredAndNothingIsReported()
    {
        var result = Run(Entities + SqlBridge + AuditEngines);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3041").Should().BeEmpty("the boundary's project references the trail's engines");
        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Values
            .Should().Contain(s => s.Contains("AddSqlAuditedEntities<global::Probe.Audit.LedgerBoundary>", StringComparison.Ordinal),
                "the control: the boundary's registration ran, and registers the phase");
    }

    [Fact]
    public void OnTheDocumentsEngine_AnAuditedEntityWithoutAuditEngines_IsPrag3114()
    {
        var result = Run(Entities + DocumentsBridge);

        var reported = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3114").ToList();
        reported.Count.Should().Be(1, "Entry is [Audited]; Note is not");
        reported[0].Severity.Should().Be(DiagnosticSeverity.Error);
        reported[0].GetMessage().Should().Contain("Entry");
        reported[0].GetMessage().Should().Contain("Pragmatic.Audit.Engines");
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3041").Should().BeEmpty("the Sql engine is not referenced");
    }

    [Fact]
    public void OnTheDocumentsEngine_WithAuditEngines_TheTrailIsRegisteredAndNothingIsReported()
    {
        var result = Run(Entities + DocumentsBridge + AuditEngines);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3114").Should().BeEmpty("the boundary's project references the trail's engines");
        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Values
            .Should().Contain(s => s.Contains("AddDocAuditedEntities<global::Probe.Audit.LedgerBoundary>", StringComparison.Ordinal),
                "the control: the boundary's registration ran, and registers the phase");
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(source, "Pragmatic.Persistence.Documents.Generated", References);

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>(),
        .. ByName("Pragmatic.Specification", "Pragmatic.Abstractions", "Pragmatic.Persistence.Expressions", "System.Linq.Queryable", "System.Linq.Expressions",
            "System.ComponentModel.Annotations", "Microsoft.Extensions.DependencyInjection.Abstractions")
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}
