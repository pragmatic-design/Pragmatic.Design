using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Cryptography;
using Pragmatic.Cryptography.Engines.Documents;
using Pragmatic.Persistence.Documents;
using Xunit;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     The per-subject keys on the documents engine (Pragmatic.Cryptography.Engines), on MongoDB: a member's <c>ProtectedValue</c> written under
///     their key, read back decrypted, and once the key is destroyed read back as erased, its ciphertext untouched; the key's document kept,
///     without the key (#431).
/// </summary>
public sealed class SubjectKeysOnTheDocumentsEngineTests(MongoServer mongo) : IClassFixture<MongoServer>
{
    [Fact]
    public void The_keys_collection_has_its_unique_key_id()
    {
        var keys = MembersBoundaryDocSchema.Collections.Single(c => c.Collection == "__SubjectKeys");
        var index = Assert.Single(keys.Indexes);
        Assert.Equal(("IX___SubjectKeys_KeyId", true), (index.Name, index.Unique));
    }

    [Fact]
    public async Task A_protected_value_reads_back_decrypted_and_once_its_key_is_destroyed_as_erased()
    {
        var session = new DocSession(mongo.Client, "k" + Guid.NewGuid().ToString("N")[..12]);
        await using var provider = Keys(session);
        await using var scope = provider.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<ISubjectDataProtector>();
        var keys = scope.ServiceProvider.GetRequiredService<ISubjectKeyStore>();
        var subject = "subject-" + Guid.NewGuid().ToString("N");
        var plain = Encoding.UTF8.GetBytes("call after six");

        var member = Member.Create();
        member.Email = subject + "@example.com";
        member.Note = new ProtectedValue(await protector.ProtectAsync(subject, plain));
        await session.InsertAsync(member);
        var key = await session.Query<SubjectKeyDocument>().FirstOrDefaultAsync((k, s) => k.SubjectRef == s, subject);
        Assert.NotNull(key);
        Assert.Equal(key.KeyId, member.Note.KeyId); // the value names the key kept for its subject

        var stored = Assert.Single(await session.Query<Member>().Where((m, email) => m.Email == email, member.Email).ToListAsync());
        var read = await protector.TryReadAsync(stored.Note.Packed);
        Assert.Equal(DecryptOutcome.Success, read.Outcome);
        Assert.Equal(plain, read.Plain);

        Assert.False((await keys.DestroyAsync(subject)).AlreadyDestroyed);
        Assert.True((await keys.DestroyAsync(subject)).AlreadyDestroyed); // idempotent: the original destruction is reported

        // The ciphertext is where it was; it is the key that is gone, and the read says erased, not tampered with.
        var after = Assert.Single(await session.Query<Member>().Where((m, email) => m.Email == email, member.Email).ToListAsync());
        Assert.Equal(member.Note.Packed, after.Note.Packed);
        Assert.Equal(DecryptOutcome.KeyDestroyed, (await protector.TryReadAsync(after.Note.Packed)).Outcome);
        await Assert.ThrowsAsync<SubjectKeyDestroyedException>(async () => await protector.ProtectAsync(subject, plain)); // never resurrected
        var destroyed = await session.Query<SubjectKeyDocument>().FirstOrDefaultAsync((k, s) => k.SubjectRef == s, subject);
        Assert.Equal((null, key.KeyId), (destroyed!.WrappedKey, destroyed.KeyId)); // the document stays, without its key
        Assert.NotNull(destroyed.DestroyedAt);
    }

    /// <summary>The keys of the members' boundary on <paramref name="session" />, as the generated host registers them beside the registry.</summary>
    private static ServiceProvider Keys(DocSession session)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<DocSession>(typeof(MembersBoundary), session); // an instance: the scope does not dispose the test's session
        services.AddSingleton<ISecretEncryptor>(new AesGcmSecretEncryptor(new EncryptionKeyRing(EncryptionKey.FromMaterial(RandomNumberGenerator.GetBytes(32)))));
        services.AddDocSubjectKeys<MembersBoundary>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
