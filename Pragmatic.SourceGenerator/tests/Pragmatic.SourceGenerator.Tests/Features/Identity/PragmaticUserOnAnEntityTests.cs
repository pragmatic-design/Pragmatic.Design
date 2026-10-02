using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Identity;

/// <summary>
///     <c>[PragmaticUser]</c> on a type that is also a <c>[Entity]</c> — the only shape the
///     repository actually has, and the one that produced nothing.
/// </summary>
/// <remarks>
///     <para>
///         <c>UserEntityTransform.ResolveKeyType</c> cannot rely on <c>IdentityUserBase&lt;TKey&gt;</c> in
///         the base chain or on a declared <c>Id</c> property. A Pragmatic entity declares neither:
///         <c>Id</c> is written by <c>EntityTraitsTemplate</c> into the same compilation, so
///         <c>GetMembers()</c> cannot see it while the transform runs. A transform that returns null there
///         leaves the whole <c>[PragmaticUser]</c> pipeline — profile adapter, resolver, culture provider —
///         producing nothing at all, silently.
///     </para>
///     <para>
///         Same wall as the permission constants in Actions and the Client manifest: a generator
///         cannot resolve a symbol it is itself about to create. The answer is the same one — read the
///         marker that will cause the member to exist.
///     </para>
///     <para>
///         <c>UserCultureProviderGeneratorTests</c> declare <c>public int Id</c> by hand, so they
///         exercise the fallback and never this shape.
///     </para>
///     <para>
///         Nothing here asserts on the resolved key type, and that is not an omission: no template reads
///         it, and the model does not carry it. Resolving the key still gates the feature — failing
///         to find one means the entity is not a usable <c>[PragmaticUser]</c> — so assert on what is
///         emitted, which is the only observable difference.
///     </para>
/// </remarks>
public class PragmaticUserOnAnEntityTests
{
    private const string Stubs = """
        namespace Pragmatic.Persistence.EFCore
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : System.Attribute { }
        }

        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EntityAttribute : System.Attribute { }

            public interface IEntity { System.Guid PersistenceId { get; } }
        }

        namespace Pragmatic.Identity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticUserAttribute : System.Attribute
            {
                public string MatchClaim { get; set; } = "sub";
                public string? MatchProperty { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class ProfilePropertyAttribute : System.Attribute { }
        }
        """;

    /// <summary>
    ///     Marks the compilation as referencing Identity.Persistence — the resolver's gate.
    /// </summary>
    /// <remarks>
    ///     The marker is <c>RolePermission</c>.
    ///     Non-generic deliberately: <c>GetTypeByMetadataName</c> needs backtick arity for a generic
    ///     type, and a marker that silently resolves to null turns the whole feature off with no
    ///     diagnostic — the failure then names the missing generated types, not the cause.
    /// </remarks>
    private const string PersistenceStub = """
        namespace Pragmatic.Identity.Persistence.Entities
        {
            public class RolePermission { }
        }
        """;

    /// <summary>Copied in shape from <c>examples/showcase/.../AppUser.cs</c>: no Id in the source.</summary>
    private const string AppUser = """

        namespace App
        {
            using Pragmatic.Identity;
            using Pragmatic.Persistence.Entity;

            [Entity]
            [PragmaticUser(MatchClaim = "sub")]
            public partial class AppUser : IEntity
            {
                public string? DisplayName { get; set; }

                [ProfileProperty]
                public string? PreferredCulture { get; set; }
            }
        }
        """;

    private static IReadOnlyDictionary<string, string> Generated(string source)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(
            GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source));

    [Fact]
    public void UserEntityWithNoDeclaredId_StillGetsItsProfileAdapter()
    {
        var files = Generated(Stubs + AppUser);

        files.Keys.Should().Contain("App.AppUser.Profile.g.cs",
            "IdentityFeature says it always generates the profile adapter, and the key type is on "
            + "[Entity] even though the Id property does not exist yet");
    }

    [Fact]
    public void UserEntityWithNoDeclaredId_GetsItsResolverWhenIdentityPersistenceIsReferenced()
    {
        var files = Generated(Stubs + PersistenceStub + AppUser);

        files.Keys.Should().Contain("App.AppUser.Resolver.g.cs",
            "the resolver is gated on Identity.Persistence only, not on how the key was resolved");
    }

    /// <summary>
    ///     The path the existing culture-provider tests take. Kept here so a change to the new attribute
    ///     lookup cannot quietly take the declared-Id fallback with it.
    /// </summary>
    [Fact]
    public void UserEntityDeclaringItsOwnId_IsUnaffected()
    {
        var declaredId = """

            namespace App
            {
                using Pragmatic.Identity;

                [PragmaticUser(MatchClaim = "sub", MatchProperty = "UserKey")]
                public partial class PlainUser
                {
                    public int Id { get; set; }
                    public string UserKey { get; set; } = "";

                    [ProfileProperty]
                    public string? PreferredCulture { get; set; }
                }
            }
            """;

        Generated(Stubs + declaredId).Keys.Should().Contain("App.PlainUser.Profile.g.cs");
    }
}
