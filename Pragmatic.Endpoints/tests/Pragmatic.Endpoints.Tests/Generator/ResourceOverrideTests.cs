using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Decorating the partial part of a scaffolded operation changes that operation, and only it.
/// </summary>
/// <remarks>
///     <para>
///         Attributes on a partial class are combined across its parts — the C# rule, not one of ours.
///         So a developer writes <c>[RequirePermission(…)] public partial class
///         ResourceCreateGuestMutation;</c> in a file of their own, keeps the generated body, and
///         changes the permission. Every operation is its own type, so Read and Delete are decorated
///         separately without an option per capability on <c>[Resource]</c>.
///     </para>
///     <para>
///         What does not happen by itself is the <i>generator</i> reading it: the scaffolded models are
///         built from the entity and nothing looks at the developer's file. Matching is by type
///         identity, which is also why a typo has to be reported — nothing collides, nothing fails to
///         compile, and the default they meant to replace quietly stays in force.
///     </para>
/// </remarks>
public class ResourceOverrideTests : EndpointsGeneratorTestBase
{
    private static string Source(string overrideDeclaration) => $$"""
        using System;
        using Pragmatic.Authorization;
        using Pragmatic.Persistence.Entity;

        namespace TestApp.Booking;

        [Boundary]
        public sealed class BookingBoundary;

        [Entity]
        [BelongsTo<BookingBoundary>]
        [Resource("guests", Capabilities = ResourceCapabilities.Create | ResourceCapabilities.Delete)]
        public partial class Guest : IEntity
        {
            public string Name { get; set; } = string.Empty;
        }

        {{overrideDeclaration}}
        """;

    private static string CreateEndpoint(string overrideDeclaration)
        => GetGeneratedSourcesAsDictionary(RunGeneratorWithPersistence(Source(overrideDeclaration)))
            .First(kv => kv.Key.Contains("_Resource.Guest.Create.Endpoint")).Value;

    /// <summary>
    ///     The declared permission replaces the scaffolded default — it is not added to it.
    /// </summary>
    /// <remarks>
    ///     Added, a developer could only ever tighten the default and never change it. That is why the
    ///     generated part carries no <c>[RequirePermission]</c> and the default travels on the model.
    /// </remarks>
    [Fact]
    public void ADeclaredPermission_ReplacesTheDefault()
    {
        var source = CreateEndpoint("""
            [RequirePermission("booking.guest.invite")]
            public partial class ResourceCreateGuestMutation;
            """);

        source.Should().Contain("booking.guest.invite");
        source.Should().NotContain("booking.guest.create",
            "the developer's permission replaces the default rather than joining it");
    }

    /// <summary>
    ///     Decorating one operation leaves the others on their defaults.
    /// </summary>
    [Fact]
    public void DecoratingOneOperation_LeavesTheOthersAlone()
    {
        var sources = GetGeneratedSourcesAsDictionary(RunGeneratorWithPersistence(Source("""
            [RequirePermission("booking.guest.invite")]
            public partial class ResourceCreateGuestMutation;
            """)));

        sources.First(kv => kv.Key.Contains("_Resource.Guest.Delete.Endpoint")).Value
            .Should().Contain("booking.guest.delete",
                "permissions are per operation, so Delete keeps its own");
    }

    /// <summary>
    ///     Untouched, the scaffolded default is what applies.
    /// </summary>
    [Fact]
    public void WithNoDeclaration_TheDefaultApplies()
        => CreateEndpoint("").Should().Contain("booking.guest.create");

    /// <summary>
    ///     A name that matches no scaffolded operation is reported (PRAG2607).
    /// </summary>
    /// <remarks>
    ///     The failure it prevents is entirely silent: an empty class of your own, and a permission
    ///     check that behaves differently from what your file says.
    /// </remarks>
    [Fact]
    public void AMistypedName_IsReported()
        => HasDiagnostic(RunGeneratorWithPersistence(Source("""
            [RequirePermission("booking.guest.invite")]
            public partial class ResourceCreateGuestMutuation;
            """)), "PRAG2607").Should().BeTrue("nothing else would say the decoration has no effect");

    /// <summary>
    ///     Control: a correctly named declaration is not reported.
    /// </summary>
    [Fact]
    public void ACorrectName_IsNotReported()
        => HasDiagnostic(RunGeneratorWithPersistence(Source("""
            [RequirePermission("booking.guest.invite")]
            public partial class ResourceCreateGuestMutation;
            """)), "PRAG2607").Should().BeFalse();

    /// <summary>
    ///     Control: an ordinary hand-written type carrying the attribute is not a mistyped override.
    /// </summary>
    /// <remarks>
    ///     The provider behind this is keyed on <c>[RequirePermission]</c>, so it sees every type in the
    ///     assembly that carries one. Without this control the check would report every action and
    ///     mutation in an application that also happens to use <c>[Resource]</c>.
    /// </remarks>
    [Fact]
    public void AnOrdinaryTypeWithAPermission_IsNotReported()
        => HasDiagnostic(RunGeneratorWithPersistence(Source("""
            [RequirePermission("booking.guest.read")]
            public partial class SomeOtherThing
            {
                public string Name { get; init; } = string.Empty;
            }
            """)), "PRAG2607").Should().BeFalse("it declares members, so it is a type of its own");
}
