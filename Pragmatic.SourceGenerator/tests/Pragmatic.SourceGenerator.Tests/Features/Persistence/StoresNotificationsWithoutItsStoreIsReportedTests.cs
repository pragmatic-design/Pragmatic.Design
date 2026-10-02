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

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source, GeneratorTestHelper.FromType<global::Pragmatic.Notifications.Attributes.StoresNotificationsAttribute>());
}
