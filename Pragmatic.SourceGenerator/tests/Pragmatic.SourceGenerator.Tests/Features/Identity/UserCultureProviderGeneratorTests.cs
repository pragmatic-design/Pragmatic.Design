using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Identity;

/// <summary>
///     The generated <c>II18NConfigProvider</c> at priority 200, and the gates that decide whether it
///     is emitted at all.
/// </summary>
/// <remarks>
///     Text-level: the stubs give <c>FeatureDetector</c> the names it switches on, not working types.
///     The provider is resolved out of a container and run against a database in
///     <c>Pragmatic.Internationalization.Tests</c>.
/// </remarks>
public class UserCultureProviderGeneratorTests
{
    /// <summary>The attributes the [PragmaticUser] pipeline triggers on.</summary>
    private const string IdentityStubs = """
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

    /// <summary>Marks the compilation as referencing Internationalization.</summary>
    private const string I18nStub = """
        namespace Pragmatic.Internationalization.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly)]
            public sealed class TranslationKeysAttribute : System.Attribute { }
        }
        """;

    /// <summary>Marks the compilation as referencing Composition — IPragmaticBuilder's gate.</summary>
    private const string CompositionStub = """
        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }
        """;

    /// <summary>All three gates satisfied.</summary>
    private const string Stubs = IdentityStubs + PersistenceStub + I18nStub + CompositionStub;

    private const string UserWithCulture = """

        namespace App
        {
            using Pragmatic.Identity;

            [PragmaticUser(MatchClaim = "sub", MatchProperty = "UserKey")]
            public partial class AppUser
            {
                public int Id { get; set; }
                public string UserKey { get; set; } = "";

                [ProfileProperty]
                public string? PreferredCulture { get; set; }
            }
        }
        """;

    private const string UserWithoutCulture = """

        namespace App
        {
            using Pragmatic.Identity;

            [PragmaticUser(MatchClaim = "sub", MatchProperty = "UserKey")]
            public partial class AppUser
            {
                public int Id { get; set; }
                public string UserKey { get; set; } = "";

                [ProfileProperty]
                public string? TimeZone { get; set; }
            }
        }
        """;

    private static IReadOnlyDictionary<string, string> Generated(string source)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(
            GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []));

    private static string? File(IReadOnlyDictionary<string, string> files, string hintFragment)
        => files.Where(kv => kv.Key.Contains(hintFragment)).Select(kv => kv.Value).FirstOrDefault();

    [Fact]
    public void UserDeclaringPreferredCulture_GetsAProviderInTheUserPreferenceBand()
    {
        var provider = File(Generated(Stubs + UserWithCulture), "AppUserCultureConfigProvider");

        provider.Should().NotBeNull(
            "the chain reserves 200-299 for user preferences and nothing occupied it");
        provider!.Should()
            .Contain("public override int Priority => 200;")
            .And.Contain(": CachedConfigProvider")
            .And.Contain("\"i18n:user:\" + _currentUser.Id");
    }

    [Fact]
    public void TheProvider_ReadsThroughTheSynchronousResolver()
    {
        // GetConfiguration() is synchronous. Blocking on the async resolver would be the sync-over-async
        // this codebase refuses; a real synchronous EF query is not the same thing.
        var files = Generated(Stubs + UserWithCulture);

        File(files, "AppUserCultureConfigProvider")!.Should().Contain("_users.GetProfile()");
        File(files, "AppUser.Resolver")!.Should()
            .Contain("public AppUser? Resolve()")
            .And.Contain("public IUserProfile? GetProfile()")
            .And.NotContain("GetAwaiter().GetResult()");
    }

    [Fact]
    public void UserWithoutAPreferredCultureProperty_GetsNoProvider()
    {
        // Nothing to resolve. A provider that always defers is a database read on every request for a
        // value that does not exist.
        var files = Generated(Stubs + UserWithoutCulture);

        File(files, "AppUserCultureConfigProvider").Should().BeNull();
        File(files, "_Infra.I18n.UserCulture").Should().BeNull();
        File(files, "AppUser.Profile").Should().NotBeNull("the profile adapter does not depend on i18n");
    }

    [Fact]
    public void WithoutInternationalization_NothingIsGeneratedAgainstItsContracts()
    {
        var withoutI18n = IdentityStubs + PersistenceStub + CompositionStub;

        File(Generated(withoutI18n + UserWithCulture), "AppUserCultureConfigProvider").Should().BeNull();
    }

    [Fact]
    public void WithoutIdentityPersistence_NothingIsGenerated()
    {
        // No resolver to read through: the provider would have no way to reach the user.
        var withoutPersistence = IdentityStubs + I18nStub + CompositionStub;

        File(Generated(withoutPersistence + UserWithCulture), "AppUserCultureConfigProvider").Should().BeNull();
    }

    [Fact]
    public void TheOptIn_IsAUseMethodOnThePragmaticBuilder()
    {
        var registration = File(Generated(Stubs + UserWithCulture), "_Infra.I18n.UserCulture");

        registration.Should().NotBeNull();
        registration!.Should()
            .Contain("UseUserCulture(this global::Pragmatic.Composition.IPragmaticBuilder builder)")
            .And.Contain("AddScoped<global::Pragmatic.Internationalization.Context.II18NConfigProvider");
    }

    [Fact]
    public void TheHost_DoesNotTurnItOnByItself()
    {
        // Every other generated registration is called by the host without being asked. This one is a
        // product decision: an application resolving culture from a header or a subdomain would
        // otherwise find a per-request database read outranking what it configured.
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            Stubs + UserWithCulture + """

            public static class Program { public static void Main() { } }
            """, []);

        var host = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Host.Services"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

        (host ?? string.Empty).Should()
            .NotContain("UseUserCulture")
            .And.NotContain("AppUserCultureConfigProvider");
    }
}
