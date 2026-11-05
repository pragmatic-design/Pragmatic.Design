using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[StoresNotifications]</c> on a boundary whose project does not reference
///     <c>Pragmatic.Notifications.EFCore</c> is reported, where it is written (PRAG2100).
/// </summary>
/// <remarks>
///     <para>
///         Without the package the generator maps nothing: the notification set is gated on it, and
///         nothing said so. Its four siblings each report the same situation — PRAG2508, PRAG0831,
///         PRAG0832, PRAG2752 — and this was the one that had failed at run time most recently:
///         <c>relation "__Notifications" does not exist</c> at the first send.
///     </para>
///     <para>
///         The control declares the package's marker type in the source, which is what
///         <c>FeatureDetector</c> asks for: with it the same boundary is quiet.
///     </para>
/// </remarks>
public sealed class StoresNotificationsWithoutItsStoreIsReportedTests
{
    private const string Boundary = """
        using Pragmatic.Notifications.Attributes;

        namespace MyApp.Messages
        {
            [StoresNotifications]
            public class MessagesBoundary { }
        }
        """;

    private const string TheStorePackage = """

        namespace Pragmatic.Notifications.EFCore { public sealed class NotificationDbContext { } }
        """;

    [Fact]
    public void WithoutTheStorePackage_TheDeclarationIsReported()
    {
        var result = Run(Boundary);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2100").Should().BeTrue(
            "the attribute maps nothing without Pragmatic.Notifications.EFCore, and has to say so");
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG2100").Single().GetMessage()
            .Should().Contain("MessagesBoundary").And.Contain("Pragmatic.Notifications.EFCore");
    }

    /// <summary>The control: with the package the declaration is honoured, and nothing is reported.</summary>
    [Fact]
    public void WithTheStorePackage_NothingIsReported()
    {
        var result = Run(Boundary + TheStorePackage);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            "a control that does not compile declares no marker, and would pass for the wrong reason");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG2100").Should().BeFalse();
    }

    // --- On the Sql and documents engines the store is Pragmatic.Notifications.Engines' ---

    private const string EngineBoundary = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Notifications.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace MyApp.Messages
        {
            [Boundary]
            [PersistenceEngine(PersistenceEngineKind.Sql)]
            [StoresNotifications]
            public sealed partial class MessagesBoundary;
        }
        """;

    /// <summary>Pragmatic.Notifications.Engines, by the type the generator recognises it by: its Sql row.</summary>
    private const string TheEnginesPackage = """

        namespace Pragmatic.Notifications.Engines.Sql { public sealed class NotificationRow { } }
        """;

    [Fact]
    public void OnAnEngineBoundaryWithoutTheEnginesPackage_TheDeclarationIsReported_NamingIt()
    {
        var result = RunOnEngine(EngineBoundary);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG2100").Single().GetMessage()
            .Should().Contain("MessagesBoundary").And.Contain("does not reference Pragmatic.Notifications.Engines");
    }

    [Fact]
    public void OnAnEngineBoundaryWithTheEnginesPackage_NothingIsReported()
    {
        var result = RunOnEngine(EngineBoundary + TheEnginesPackage);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            "a control that does not compile declares no marker, and would pass for the wrong reason");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG2100").Should().BeFalse();
    }

    /// <summary>The EF Core package does not stand in for the engines': on an engine it maps nothing, so the declaration is still reported.</summary>
    [Fact]
    public void OnAnEngineBoundaryWithOnlyTheEFCorePackage_TheDeclarationIsReported()
    {
        var result = RunOnEngine(EngineBoundary + TheStorePackage);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2100").Should().BeTrue();
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source, GeneratorTestHelper.FromType<global::Pragmatic.Notifications.Attributes.StoresNotificationsAttribute>());

    private static SourceGenRunResult RunOnEngine(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source, GeneratorTestHelper.FromType<global::Pragmatic.Notifications.Attributes.StoresNotificationsAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Entity.EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>());
}
