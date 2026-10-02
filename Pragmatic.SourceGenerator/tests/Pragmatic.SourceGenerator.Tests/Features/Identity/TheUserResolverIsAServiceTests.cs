using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Identity;

/// <summary>
///     The generated <c>{User}Resolver</c> is registered like any <c>[Service]</c> of the module, so an
///     action, a mutation or a service can take it as a dependency.
/// </summary>
/// <remarks>
///     <para>
///         A resolver that is generated but not injectable pushes each application to write its own
///         lookup — a <c>CurrentEmployee</c> repeating the resolver's predicate file after file, the shape
///         the resolver's template warns about: two copies of "which claim identifies the user", one of
///         which can stop matching in silence.
///     </para>
///     <para>
///         Text-level, with the same stubs as <see cref="UserCultureProviderGeneratorTests" />: the
///         subject is what the module publishes to its host — <c>_Metadata.DI</c>, which the host reads
///         to register the module's services — and the module's own registration.
///     </para>
/// </remarks>
public class TheUserResolverIsAServiceTests
{
    private const string IdentityStubs = """
        namespace Pragmatic.Identity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticUserAttribute : System.Attribute
            {
                public string MatchClaim { get; set; } = "sub";
                public string? MatchProperty { get; set; }
            }
        }
        """;

    /// <summary>Marks the compilation as referencing Identity.Persistence — the resolver's gate.</summary>
    private const string PersistenceStub = """
        namespace Pragmatic.Identity.Persistence.Entities
        {
            public class RolePermission { }
        }
        """;

    /// <summary>Marks the compilation as referencing Composition — what registers services at all.</summary>
    private const string CompositionStub = """
        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }
        """;

    private static string User(string accessibility) => $$"""

        namespace App
        {
            using Pragmatic.Identity;

            [PragmaticUser(MatchClaim = "sub", MatchProperty = "UserKey")]
            {{accessibility}} partial class AppUser
            {
                public int Id { get; set; }
                public string UserKey { get; set; } = "";
            }
        }
        """;

    private static IReadOnlyDictionary<string, string> Generated(string source)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(
            GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []));

    private static string? File(IReadOnlyDictionary<string, string> files, string hintFragment)
        => files.Where(kv => kv.Key.Contains(hintFragment)).Select(kv => kv.Value).FirstOrDefault();

    [Fact]
    public void AUserEntity_ItsResolverIsPublishedToTheHostAsAScopedService()
    {
        var files = Generated(IdentityStubs + PersistenceStub + CompositionStub + User("public"));

        var metadata = File(files, "_Metadata.DI");
        metadata.Should().NotBeNull("the module publishes its services to the host through this metadata");
        metadata!.Should().Contain("\"implementation\": \"global::App.AppUserResolver\"");
        metadata.Should().Contain("\"interface\": \"global::App.AppUserResolver\"");
        metadata.Should().Contain("\"lifetime\": \"Scoped\"",
            "it reads the current user: a singleton would capture the first request's");
    }

    [Fact]
    public void AUserEntity_TheModuleRegistersItsResolver()
    {
        var files = Generated(IdentityStubs + PersistenceStub + CompositionStub + User("public"));

        File(files, "_Infra.DI.ServiceRegistration").Should()
            .Contain("services.TryAddScoped<global::App.AppUserResolver, global::App.AppUserResolver>();");
    }

    [Fact]
    public void TheResolver_FindsAUserByAGivenIdentityKey_WithTheSamePredicate()
    {
        var resolver = File(Generated(IdentityStubs + PersistenceStub + CompositionStub + User("public")),
            "AppUser.Resolver");

        resolver.Should().NotBeNull();
        resolver!.Should().Contain("public async Task<AppUser?> FindByIdentityKeyAsync(string identityKey, CancellationToken ct = default)");
        resolver.Should().Contain(".FirstOrDefaultAsync(u => u.UserKey == identityKey, ct)",
            "the sign-in finds the user by the key it is given, before anyone is authenticated — on the "
            + "same property the current-user lookup matches");
    }

    /// <summary>The control: no Identity.Persistence, no resolver, and so nothing to register.</summary>
    /// <remarks>A registration naming a type this generator did not write would not compile.</remarks>
    [Fact]
    public void WithoutIdentityPersistence_NothingNamesAResolver()
    {
        var files = Generated(IdentityStubs + CompositionStub + User("public"));

        File(files, "AppUser.Resolver").Should().BeNull("the resolver needs the repository the package provides");
        files.Values.Should().NotContain(source => source.Contains("AppUserResolver"));
    }

    /// <summary>
    ///     The host registers a module's services by naming them from its own assembly: an internal
    ///     resolver cannot be named there, so it is not published.
    /// </summary>
    [Fact]
    public void AnInternalUserEntity_ItsResolverIsNotPublished()
    {
        var files = Generated(IdentityStubs + PersistenceStub + CompositionStub + User("internal"));

        File(files, "AppUser.Resolver").Should().NotBeNull("the resolver is still generated for the module's own use");
        (File(files, "_Metadata.DI") ?? "").Should().NotContain("AppUserResolver");
    }
}
