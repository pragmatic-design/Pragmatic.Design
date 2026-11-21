using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[EnableJobPersistence]</c> on a boundary whose project does not reference the job store package of the boundary's engine is
///     reported, naming it (PRAG2508): Pragmatic.Jobs.EFCore on EF Core, Pragmatic.Jobs.Engines on the Sql and documents engines (#904).
/// </summary>
/// <remarks>
///     Before the engines had a store the warning named Pragmatic.Jobs.EFCore on every boundary, so an engine boundary that referenced the
///     right package was told to reference the wrong one. The controls declare each package's marker type, which is what the generator asks for.
/// </remarks>
public sealed class JobPersistenceWithoutItsStoreIsReportedTests
{
    private const string EngineBoundary = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Jobs.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace MyApp.Work
        {
            [Boundary]
            [PersistenceEngine(PersistenceEngineKind.Sql)]
            [EnableJobPersistence]
            public sealed partial class WorkBoundary;
        }
        """;

    /// <summary>Pragmatic.Jobs.Engines, by the type the generator recognises it by: its Sql job row.</summary>
    private const string TheEnginesPackage = """

        namespace Pragmatic.Jobs.Engines.Sql { public sealed class JobRow { } }
        """;

    /// <summary>Pragmatic.Jobs.EFCore, by the type the generator recognises it by.</summary>
    private const string TheEfCorePackage = """

        namespace Pragmatic.Jobs.EFCore.Entities { public sealed class JobEntityTypeConfiguration { } }
        """;

    [Fact]
    public void OnAnEngineBoundaryWithoutTheEnginesPackage_TheDeclarationIsReported_NamingIt()
    {
        var result = Run(EngineBoundary);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG2508").Single().GetMessage()
            .Should().Contain("WorkBoundary").And.Contain("does not reference Pragmatic.Jobs.Engines");
    }

    [Fact]
    public void OnAnEngineBoundaryWithTheEnginesPackage_NothingIsReported()
    {
        var result = Run(EngineBoundary + TheEnginesPackage);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            "a control that does not compile declares no marker, and would pass for the wrong reason");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG2508").Should().BeFalse();
    }

    /// <summary>The EF Core package does not stand in for the engines': on an engine it maps nothing, so the declaration is still reported.</summary>
    [Fact]
    public void OnAnEngineBoundaryWithOnlyTheEFCorePackage_TheDeclarationIsReported()
    {
        var result = Run(EngineBoundary + TheEfCorePackage);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2508").Should().BeTrue();
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source, GeneratorTestHelper.FromType<global::Pragmatic.Jobs.Attributes.EnableJobPersistenceAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Entity.EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>());
}
