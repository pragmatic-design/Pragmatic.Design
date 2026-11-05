using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[EnableSagaPersistence]</c> on a boundary the Sql or documents engine persists (#529): its store is Pragmatic.Messaging.Engines', and
///     PRAG0832 names that package when it is missing, as PRAG0831 does for the outbox; with it the boundary is quiet.
/// </summary>
public sealed class SagaPersistenceOnTheEnginesIsReportedTests
{
    private const string EngineBoundary = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace MyApp.Front
        {
            [Boundary]
            [PersistenceEngine(PersistenceEngineKind.Sql)]
            [Pragmatic.Messaging.Attributes.EnableSagaPersistence]
            public sealed partial class FrontBoundary;
        }
        """;

    /// <summary>Pragmatic.Messaging.Engines' saga store, by the type the generator recognises it by: its Sql instance row.</summary>
    private const string TheEnginesStore = """

        namespace Pragmatic.Messaging.Engines.Sql { public sealed class SagaInstanceRow { } }
        """;

    /// <summary>Pragmatic.Messaging.EFCore, by the type the generator recognises it by.</summary>
    private const string TheEfCoreStore = """

        namespace Pragmatic.Messaging.EFCore { public sealed class EfCoreOutboxSource { } }
        """;

    [Fact]
    public void WithoutTheEnginesPackage_TheDeclarationIsReported_NamingIt()
    {
        var result = Run(EngineBoundary);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0832").Single().GetMessage()
            .Should().Contain("FrontBoundary").And.Contain("does not reference Pragmatic.Messaging.Engines");
    }

    [Fact]
    public void WithTheEnginesPackage_NothingIsReported()
    {
        var result = Run(EngineBoundary + TheEnginesStore);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            "a control that does not compile declares no marker, and would pass for the wrong reason");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0832").Should().BeFalse();
    }

    /// <summary>The EF Core package does not stand in for the engines': on an engine it maps nothing.</summary>
    [Fact]
    public void WithOnlyTheEfCorePackage_TheDeclarationIsReported()
    {
        var result = Run(EngineBoundary + TheEfCoreStore);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0832").Should().BeTrue();
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source,
            GeneratorTestHelper.FromType<global::Pragmatic.Messaging.Attributes.EnableSagaPersistenceAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Entity.EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>());
}
