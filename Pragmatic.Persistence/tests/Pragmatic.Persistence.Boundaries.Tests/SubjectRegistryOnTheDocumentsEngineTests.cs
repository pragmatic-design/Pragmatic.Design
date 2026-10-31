using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Cryptography;
using Pragmatic.Persistence.Documents;
using Pragmatic.Privacy;
using Pragmatic.Privacy.Engines.Documents;
using Xunit;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     The subject registry and the consent store on the documents engine (Pragmatic.Privacy.Engines), on MongoDB: their collections in the
///     schema of the boundary that holds a <c>[DataSubject]</c>, and a blind index unique among the known subjects through its lookup's id, so
///     any number of subjects can be forgotten.
/// </summary>
public sealed class SubjectRegistryOnTheDocumentsEngineTests(MongoServer mongo) : IClassFixture<MongoServer>
{
    [Fact]
    public void The_boundarys_schema_holds_the_registrys_collections()
        => Assert.Equal(["__Consents", "__SubjectLookups", "__Subjects"],
            MembersBoundaryDocSchema.Collections.Select(c => c.Collection).Where(c => c.StartsWith("__", StringComparison.Ordinal)).Order());

    [Fact]
    public async Task References_are_found_resolved_and_forgotten_and_any_number_of_subjects_can_be_forgotten()
    {
        await using var provider = Registry(new DocSession(mongo.Client, "a" + Guid.NewGuid().ToString("N")[..12]));
        await using var scope = provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<ISubjectRegistry>();

        var reference = await registry.GetOrCreateReferenceAsync("Member", "ada@example.com");
        Assert.Equal(reference, await registry.GetOrCreateReferenceAsync("Member", "ada@example.com"));
        Assert.NotEqual(reference, await registry.GetOrCreateReferenceAsync("Employee", "ada@example.com"));
        Assert.Equal(reference, await registry.FindReferenceAsync("Member", "ada@example.com"));
        Assert.Equal("ada@example.com", await registry.ResolveIdentityAsync(reference));

        var other = await registry.GetOrCreateReferenceAsync("Member", "bob@example.com");
        Assert.True(await registry.ForgetAsync(reference));
        Assert.True(await registry.ForgetAsync(other));
        Assert.False(await registry.ForgetAsync(reference));
        Assert.Null(await registry.ResolveIdentityAsync(reference));
        Assert.Null(await registry.FindReferenceAsync("Member", "ada@example.com"));

        var returned = await registry.GetOrCreateReferenceAsync("Member", "ada@example.com");
        Assert.NotEqual(reference, returned);
        Assert.Equal("ada@example.com", await registry.ResolveIdentityAsync(returned));
    }

    [Fact]
    public async Task A_consent_is_per_notice_version_withdrawn_for_every_version_and_kept_in_the_history()
    {
        await using var provider = Registry(new DocSession(mongo.Client, "a" + Guid.NewGuid().ToString("N")[..12]));
        await using var scope = provider.CreateAsyncScope();
        var consents = scope.ServiceProvider.GetRequiredService<IConsentStore>();
        var subject = SubjectLookup.NewReference();

        await consents.GrantAsync(subject, "newsletter", "v1", "signup form");
        await consents.GrantAsync(subject, "newsletter", "v2");
        Assert.True(await consents.IsGrantedAsync(subject, "newsletter", "v1"));
        Assert.False(await consents.IsGrantedAsync(subject, "newsletter", "v3"));

        Assert.True(await consents.WithdrawAsync(subject, "newsletter", DateTimeOffset.UtcNow));
        Assert.False(await consents.IsGrantedAsync(subject, "newsletter", "v2"));
        Assert.False(await consents.WithdrawAsync(subject, "newsletter", DateTimeOffset.UtcNow));

        await consents.GrantAsync(subject, "newsletter", "v1");
        var history = await consents.GetHistoryAsync(subject);
        Assert.Equal([("v1", true, "signup form"), ("v2", false, null)], history.OrderBy(c => c.NoticeVersion).Select(c => (c.NoticeVersion, c.IsActive, c.Source)));
    }

    /// <summary>The registry of the members' boundary on <paramref name="session" />, as the generated host registers it when it is the only place.</summary>
    private static ServiceProvider Registry(DocSession session)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<DocSession>(typeof(MembersBoundary), session); // an instance: the scope does not dispose the test's session
        services.AddSingleton<ISecretEncryptor>(new AesGcmSecretEncryptor(new EncryptionKeyRing(EncryptionKey.FromMaterial(RandomNumberGenerator.GetBytes(32)))));
        services.AddSingleton<ISubjectLookupKeyProvider>(new FixedLookupKey());
        services.AddDocSubjectRegistry<MembersBoundary>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class FixedLookupKey : ISubjectLookupKeyProvider
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        public ValueTask<byte[]> GetLookupKeyAsync(CancellationToken ct = default) => ValueTask.FromResult(_key);
    }
}
