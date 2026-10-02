using System.Security.Cryptography;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Cryptography;
using Pragmatic.Privacy.EFCore;

namespace Pragmatic.Privacy.Tests.Unit;

/// <summary>
///     That <c>AddPrivacy</c> produces a container the services can actually be resolved from.
/// </summary>
/// <remarks>
///     Registration is the kind of code that compiles and then fails at the first request. These
///     resolve rather than merely assert a descriptor exists, because a missing transitive dependency
///     only shows up on resolution.
/// </remarks>
public class PrivacyRegistrationTests
{
    private sealed class NoSubjects : ISubjectRegistry
    {
        public ValueTask<string> GetOrCreateReferenceAsync(string subjectType, string identifier, CancellationToken ct = default)
            => new("ref");

        public ValueTask<string?> FindReferenceAsync(string subjectType, string identifier, CancellationToken ct = default)
            => new((string?)null);

        public ValueTask<string?> ResolveIdentityAsync(string subjectRef, CancellationToken ct = default)
            => new((string?)null);

        public ValueTask<bool> ForgetAsync(string subjectRef, CancellationToken ct = default) => new(true);
    }

    private sealed class NoConsents : IConsentStore
    {
        public ValueTask GrantAsync(
            string subjectRef, string purpose, string noticeVersion, string? source = null,
            CancellationToken ct = default) => default;

        public ValueTask<bool> WithdrawAsync(
            string subjectRef, string purpose, DateTimeOffset now, CancellationToken ct = default) => new(true);

        public ValueTask<bool> IsGrantedAsync(
            string subjectRef, string purpose, string noticeVersion, CancellationToken ct = default) => new(false);

        public ValueTask<IReadOnlyList<ConsentRecord>> GetHistoryAsync(
            string subjectRef, CancellationToken ct = default)
            => new((IReadOnlyList<ConsentRecord>)[]);
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISubjectRegistry, NoSubjects>();
        services.AddSingleton<IConsentStore, NoConsents>();
        services.AddPrivacy();
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void TheProcessServices_Resolve()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ErasureOrchestrator>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<SubjectAccessService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ProcessingRegisterBuilder>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IPortabilityFormatter>().Should().BeOfType<JsonPortabilityFormatter>();
    }

    /// <summary>
    ///     An operation reaches the data-subject processes through an interface, as it reaches everything
    ///     else: the generator does not inject a concrete type (PRAG0419), so with the classes alone no
    ///     endpoint could export, erase or show the register.
    /// </summary>
    [Fact]
    public void TheProcessServices_ResolveByInterface_AsTheSameInstances()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<ISubjectErasure>().Should().BeSameAs(services.GetRequiredService<ErasureOrchestrator>());
        services.GetRequiredService<ISubjectAccess>().Should().BeSameAs(services.GetRequiredService<SubjectAccessService>());
        services.GetRequiredService<IProcessingRegisterBuilder>().Should().BeSameAs(services.GetRequiredService<ProcessingRegisterBuilder>());
    }

    [Fact]
    public void WithNoContributedSteps_ErasureStillResolves()
    {
        // An application adopting this incrementally has no steps on day one. Requiring at least one
        // would make the first move "register something that does nothing".
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ErasureOrchestrator>().Should().NotBeNull();
    }

    [Fact]
    public void TheRetentionResolver_IsNotRegistered()
    {
        // Deliberate: it needs the current notice version, and a wrong default would evaluate consent
        // against a notice the subject never saw while looking like it worked.
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IRetentionPolicyResolver>().Should().BeNull();
    }

    /// <summary>
    ///     An application that registers no store still starts, and still gets the register.
    /// </summary>
    /// <remarks>
    ///     Classifying a field generates an <c>IPersonalDataSource</c> and an <c>IErasureStep</c> per
    ///     entity, and both take an <c>ISubjectRegistry</c>. Requiring one to build the container forced
    ///     every application that wanted the Article 30 document — which uses no store at all — to stand
    ///     up a database, an encryptor and a lookup key first.
    /// </remarks>
    [Fact]
    public void WithNoSubjectStore_TheContainerStillBuildsAndTheRegisterResolves()
    {
        var services = new ServiceCollection();
        services.AddPrivacy();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ProcessingRegisterBuilder>().Should().NotBeNull();
    }

    [Fact]
    public async Task WithNoSubjectStore_UsingTheRegistryFailsAndNamesWhatToRegister()
    {
        // Throws rather than answering empty: an access request reporting "no data" and an erasure
        // reporting success are exactly what an unconfigured subject-rights process must not do.
        var services = new ServiceCollection();
        services.AddPrivacy();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ISubjectRegistry>();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.GetOrCreateReferenceAsync("Member", "alice"));

        thrown.Message.Should().Contain("AddSubjectRegistry()");
    }

    /// <summary>
    ///     The EF store wins over the fallback when it is registered after it — the order a Pragmatic host
    ///     produces: the generated wiring calls <c>AddPrivacy()</c> (through
    ///     <c>AddGeneratedPrivacyAdapters()</c>) before the application's own registrations run.
    /// </summary>
    /// <remarks>
    ///     Both used <c>TryAdd</c>, so whichever came first stayed: the fallback, every time, and the
    ///     registry threw on its first call in an application that had registered it.
    /// </remarks>
    [Fact]
    public void TheEfStore_RegisteredAfterTheFallback_StillWins()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        using var encryptor = new AesGcmSecretEncryptor(
            new EncryptionKeyRing(EncryptionKey.FromMaterial(RandomNumberGenerator.GetBytes(32))));

        var services = new ServiceCollection();
        services.AddPrivacy();
        services.AddDbContext<PrivacyDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<ISecretEncryptor>(encryptor);
        services.AddSingleton<ISubjectLookupKeyProvider, AnyLookupKey>();
        services.AddSubjectRegistry();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISubjectRegistry>().Should().BeOfType<EfCoreSubjectRegistry>();
        scope.ServiceProvider.GetRequiredService<IConsentStore>().Should().BeOfType<EfCoreConsentStore>();
    }

    private sealed class AnyLookupKey : ISubjectLookupKeyProvider
    {
        public ValueTask<byte[]> GetLookupKeyAsync(CancellationToken ct = default) => new(new byte[32]);
    }

    [Fact]
    public void AStoreTheApplicationRegisters_WinsOverTheFallback()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISubjectRegistry>().Should().BeOfType<NoSubjects>();
    }

    [Fact]
    public void CallingItTwice_DoesNotDuplicateRegistrations()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISubjectRegistry, NoSubjects>();
        services.AddSingleton<IConsentStore, NoConsents>();
        services.AddPrivacy();
        services.AddPrivacy();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        provider.GetServices<IPortabilityFormatter>().Should().ContainSingle();
    }
}
