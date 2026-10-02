using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Cryptography;
using Pragmatic.Privacy.EFCore;

namespace Pragmatic.Privacy.Tests;

/// <summary>
///     Resolving an identity that merely appeared — in a failed login, say — without letting a stranger
///     create records about themselves by typing.
/// </summary>
public sealed class ObservedIdentityResolverTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PrivacyDbContext _db;
    private readonly AesGcmSecretEncryptor _encryptor;
    private readonly EfCoreSubjectRegistry _registry;
    private readonly ObservedIdentityResolver _resolver;

    public ObservedIdentityResolverTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _db = new PrivacyDbContext(
            new DbContextOptionsBuilder<PrivacyDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _encryptor = new AesGcmSecretEncryptor(new EncryptionKeyRing(
            EncryptionKey.FromMaterial(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))));

        _registry = new EfCoreSubjectRegistry(_db, _encryptor, new FixedLookupKey(), TimeProvider.System);
        _resolver = new ObservedIdentityResolver(_registry);
    }

    public void Dispose()
    {
        _db.Dispose();
        _encryptor.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task AKnownIdentity_ResolvesToItsExistingReference()
    {
        var expected = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");

        var resolved = await _resolver.ResolveAsync("Customer", "ada@example.com");

        resolved.Should().Be(expected, "an attempt against a real account can be correlated with its history");
    }

    [Fact]
    public async Task AnUnknownIdentity_ResolvesToNothing()
    {
        var resolved = await _resolver.ResolveAsync("Customer", "stranger@example.com");

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task AnUnknownIdentity_CreatesNoSubjectRecord()
    {
        // The defect this type exists to prevent: pseudonymising whatever a stranger typed would let
        // anyone fill the registry, and would make the system hold records about people who never had
        // a relationship with it — creating personal data as a side effect of rejecting them.
        await _resolver.ResolveAsync("Customer", "attacker-1@example.com");
        await _resolver.ResolveAsync("Customer", "attacker-2@example.com");
        _db.ChangeTracker.Clear();

        (await _db.Subjects.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnErasedIdentity_ResolvesToNothing()
    {
        // After an erasure the identity is no longer linked to anything, and a failed login must not
        // resurrect the connection.
        var reference = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");
        await _registry.ForgetAsync(reference);

        (await _resolver.ResolveAsync("Customer", "ada@example.com")).Should().BeNull();
    }

    [Fact]
    public async Task TheSameIdentityInAnotherRole_DoesNotResolve()
    {
        await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");

        (await _resolver.ResolveAsync("Employee", "ada@example.com")).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GarbageInput_ResolvesToNothingWithoutThrowing(string? identity)
    {
        // Failed logins carry whatever was submitted. Throwing here would turn a rejected attempt into
        // an unhandled error on the authentication path.
        (await _resolver.ResolveAsync("Customer", identity!)).Should().BeNull();
    }

    private sealed class FixedLookupKey : ISubjectLookupKeyProvider
    {
        private readonly byte[] _key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

        public ValueTask<byte[]> GetLookupKeyAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_key);
    }
}
