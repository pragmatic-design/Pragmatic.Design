using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition.Hosting;
using Pragmatic.Identity;
using Pragmatic.Internationalization.AspNetCore.Extensions;
using Pragmatic.Internationalization.Context;
// The generated UseUserCulture() lives in {AssemblyName}.Generated, which is not an enclosing scope.
using Pragmatic.Internationalization.Tests.Generated;
using Pragmatic.Internationalization.Types;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Internationalization.Tests.UserCulture;

/// <summary>
///     The generated per-user culture provider, resolved out of a real container against a real
///     database.
/// </summary>
/// <remarks>
///     <para>
///         The chain reserved 200–299 for user preferences in two places and nothing occupied it.
///         A test that asserted "the provider is registered" would not have caught that: the question
///         is whether <em>two</em> users resolve <em>two</em> cultures, and whether the switch being off
///         means both fall back to the system's.
///     </para>
///     <para>
///         <b>Every test uses its own subject.</b> <c>CachedConfigProvider</c> keys a cache that is
///         static per provider type and survives the container, so reusing a subject would let one
///         test's answer be served to the next.
///     </para>
/// </remarks>
public sealed class GeneratedUserCultureTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync().ConfigureAwait(false);

        using var db = NewDbContext();
        await db.Database.EnsureCreatedAsync().ConfigureAwait(false);

        db.Users.AddRange(
            new AppUser { UserKey = "user-italian", PreferredCulture = "it-IT" },
            new AppUser { UserKey = "user-french", PreferredCulture = "fr-FR" },
            new AppUser { UserKey = "user-undecided", PreferredCulture = null },
            new AppUser { UserKey = "user-nonsense", PreferredCulture = "not-a-culture" });

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync().ConfigureAwait(false);

    private UserDbContext NewDbContext()
        => new(new DbContextOptionsBuilder<UserDbContext>().UseSqlite(_connection).Options);

    /// <summary>
    ///     Builds an application: system culture en-US, optionally with the generated user provider on.
    /// </summary>
    private ServiceProvider BuildApp(string subject, bool useUserCulture)
    {
        var services = new ServiceCollection();

        services.AddPragmaticInternationalization(options =>
        {
            options.DefaultUICulture = CultureCode.EnglishUS;
        });

        services.AddSingleton(_ => new DbContextOptionsBuilder<UserDbContext>()
            .UseSqlite(_connection).Options);
        services.AddScoped<UserDbContext>();
        // The repository, which is what the resolver asks for. No DbContext registration: a Pragmatic
        // application does not have one, and registering it here would let a resolver that depends
        // on a DbContext look workable.
        services.AddScoped<IReadRepository<AppUser>, UserRepository>();
        services.AddScoped<AppUserResolver>();
        services.AddSingleton<ICurrentUser>(new SignedInUser(subject));

        if (useUserCulture)
        {
            // Exactly the line an application writes, against the real builder type.
            var builder = new PragmaticBuilder(
                services, new ConfigurationBuilder().Build(), new TestEnvironment());
            builder.UseUserCulture();
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private CultureCode? ResolveCulture(string subject, bool useUserCulture)
    {
        using var app = BuildApp(subject, useUserCulture);
        using var scope = app.CreateScope();

        return scope.ServiceProvider.GetRequiredService<I18NConfigResolver>().Resolve().DefaultUICulture;
    }

    [Fact]
    public void TwoUsersWithDifferentPreferences_ResolveTwoDifferentCultures()
    {
        ResolveCulture("user-italian", useUserCulture: true)
            .Should().Be(CultureCode.FromString("it-IT"));

        ResolveCulture("user-french", useUserCulture: true)
            .Should().Be(CultureCode.FromString("fr-FR"));
    }

    [Fact]
    public void WithTheProviderOff_BothUsersGetTheSystemCulture()
    {
        // The same two users, the same database, the one call missing. Off is the default.
        ResolveCulture("user-italian", useUserCulture: false).Should().Be(CultureCode.EnglishUS);
        ResolveCulture("user-french", useUserCulture: false).Should().Be(CultureCode.EnglishUS);
    }

    [Fact]
    public void UserWithNoPreference_DefersToTheSystemCulture()
    {
        // Null means "I have not asked for anything", not "override with nothing".
        ResolveCulture("user-undecided", useUserCulture: true).Should().Be(CultureCode.EnglishUS);
    }

    [Fact]
    public void StoredValueThatNoLongerParses_DefersInsteadOfFailingTheRequest()
    {
        // Throwing here would lock the user out of the page where they could correct the value.
        ResolveCulture("user-nonsense", useUserCulture: true).Should().Be(CultureCode.EnglishUS);
    }

    [Fact]
    public void UnknownUser_DefersToTheSystemCulture()
    {
        ResolveCulture("nobody-by-that-name", useUserCulture: true).Should().Be(CultureCode.EnglishUS);
    }

    [Fact]
    public void TheProvider_SitsInTheBandTheChainReservesForUserPreferences()
    {
        using var app = BuildApp("user-italian", useUserCulture: true);
        using var scope = app.CreateScope();

        var providers = scope.ServiceProvider.GetServices<II18NConfigProvider>().ToList();

        providers.Should().ContainSingle(p => p is AppUserCultureConfigProvider);
        providers.Single(p => p is AppUserCultureConfigProvider).Priority.Should().Be(200);
    }

    [Fact]
    public void TheProvider_IsScoped()
    {
        // Singleton would capture the first request's user and answer everyone with their preference.
        var descriptor = Descriptors().Single(d => d.ImplementationType == typeof(AppUserCultureConfigProvider));

        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
        return;

        IEnumerable<ServiceDescriptor> Descriptors()
        {
            var services = new ServiceCollection();
            new PragmaticBuilder(services, new ConfigurationBuilder().Build(), new TestEnvironment())
                .UseUserCulture();
            return services;
        }
    }

    /// <summary>Minimal <see cref="IHostEnvironment" /> — the builder needs one, nothing reads it here.</summary>
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Pragmatic.Internationalization.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
