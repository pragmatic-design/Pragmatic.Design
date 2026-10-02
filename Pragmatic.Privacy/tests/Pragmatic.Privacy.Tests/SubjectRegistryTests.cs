using System.Security.Cryptography;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Cryptography;
using Pragmatic.Privacy.EFCore;

namespace Pragmatic.Privacy.Tests;

/// <summary>
///     The registry that makes an erasure possible without destroying the record of it: references stay,
///     the person behind them does not.
/// </summary>
public sealed class SubjectRegistryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PrivacyDbContext _db;
    private readonly AesGcmSecretEncryptor _encryptor;
    private readonly EfCoreSubjectRegistry _registry;

    public SubjectRegistryTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _db = new PrivacyDbContext(
            new DbContextOptionsBuilder<PrivacyDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _encryptor = new AesGcmSecretEncryptor(
            new EncryptionKeyRing(EncryptionKey.FromMaterial(RandomNumberGenerator.GetBytes(32))));

        _registry = new EfCoreSubjectRegistry(
            _db, _encryptor, new FixedLookupKey(), TimeProvider.System);
    }

    public void Dispose()
    {
        _db.Dispose();
        _encryptor.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetOrCreate_FirstSight_AllocatesAReference()
    {
        var reference = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");

        reference.Should().NotBeNullOrWhiteSpace();
        reference.Should().MatchRegex("^[0-9a-f]{32}$", "the reference is 16 random bytes as lowercase hex");
    }

    [Fact]
    public async Task GetOrCreate_TheReferenceIsRandom_NotDerivedFromTheIdentity()
    {
        // What "the reference reveals nothing about the person" actually means, and the property an
        // erasure depends on: a derived reference — a hash of the address, say — would survive the
        // deletion of the row and let anyone holding the address recompute it.
        //
        // Asserting that the reference does not contain "ada" is the wrong test. Those are hex
        // characters, so a random reference contains them roughly once in 137 runs and the test fails
        // for a coincidence. Worse, passing it proves nothing: a random string satisfies a substring
        // check by construction, whether or not it is derived from anything.
        var first = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");

        using var otherConnection = new SqliteConnection("Filename=:memory:");
        otherConnection.Open();
        using var otherDb = new PrivacyDbContext(
            new DbContextOptionsBuilder<PrivacyDbContext>().UseSqlite(otherConnection).Options);
        otherDb.Database.EnsureCreated();

        var elsewhere = new EfCoreSubjectRegistry(
            otherDb, _encryptor, new FixedLookupKey(), TimeProvider.System);

        var second = await elsewhere.GetOrCreateReferenceAsync("Customer", "ada@example.com");

        second.Should().NotBe(first,
            "same identity, same lookup key, same encryptor — only randomness can tell them apart");
    }

    [Fact]
    public async Task GetOrCreate_SameIdentityTwice_ReturnsTheSameReference()
    {
        var first = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");
        var second = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");

        second.Should().Be(first);
    }

    [Fact]
    public async Task GetOrCreate_SameIdentityInAnotherRole_IsADifferentSubject()
    {
        // Two relationships. Erasing the customer must not erase the employee.
        var customer = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");
        var employee = await _registry.GetOrCreateReferenceAsync("Employee", "ada@example.com");

        employee.Should().NotBe(customer);
    }

    [Fact]
    public async Task Resolve_ReturnsTheIdentityBehindAReference()
    {
        var reference = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");

        (await _registry.ResolveIdentityAsync(reference)).Should().Be("ada@example.com");
    }

    [Fact]
    public async Task StoredIdentity_IsNotReadableFromTheRow()
    {
        var reference = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");
        _db.ChangeTracker.Clear();

        var row = await _db.Subjects.SingleAsync(s => s.SubjectRef == reference);

        System.Text.Encoding.UTF8.GetString(row.Identifier!).Should().NotContain("ada@example.com");
    }

    // =========================================================================
    // Erasure
    // =========================================================================

    [Fact]
    public async Task Forget_BreaksTheLinkButKeepsTheReference()
    {
        var reference = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");

        (await _registry.ForgetAsync(reference)).Should().BeTrue();
        _db.ChangeTracker.Clear();

        var row = await _db.Subjects.SingleAsync(s => s.SubjectRef == reference);
        row.Identifier.Should().BeNull();
        row.LookupIndex.Should().BeNull();
        row.ForgottenAt.Should().NotBeNull();
        row.SubjectRef.Should().Be(reference, "an audit trail full of this reference must stay meaningful");
    }

    [Fact]
    public async Task Resolve_AfterErasure_ReturnsNothing()
    {
        // The observable proof that the link is gone: the reference still exists everywhere it was
        // written, and nothing can turn it back into a person.
        var reference = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");
        await _registry.ForgetAsync(reference);

        (await _registry.ResolveIdentityAsync(reference)).Should().BeNull();
    }

    [Fact]
    public async Task Find_AfterErasure_NoLongerMatchesTheIdentity()
    {
        var reference = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");
        await _registry.ForgetAsync(reference);

        (await _registry.FindReferenceAsync("Customer", "ada@example.com")).Should().BeNull();
    }

    [Fact]
    public async Task AnErasedIdentityComingBack_IsANewSubject_NotTheOldOne()
    {
        // Recognising the returning identity would require keeping something derived from it — and
        // keeping that is still processing their data, so the erasure would not have been one.
        var original = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");
        await _registry.ForgetAsync(original);

        var returned = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");

        returned.Should().NotBe(original);
        (await _registry.ResolveIdentityAsync(original)).Should().BeNull("the old reference stays erased");
        (await _registry.ResolveIdentityAsync(returned)).Should().Be("ada@example.com");
    }

    [Fact]
    public async Task Forget_Twice_ReportsThatThereWasNothingLeftToDo()
    {
        var reference = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");
        await _registry.ForgetAsync(reference);

        (await _registry.ForgetAsync(reference)).Should().BeFalse();
    }

    [Fact]
    public async Task Forget_UnknownReference_ReportsFalse()
        => (await _registry.ForgetAsync("00000000000000000000000000000000")).Should().BeFalse();

    [Fact]
    public async Task Resolve_UnknownReference_ReturnsNothing()
    {
        // Indistinguishable from an erased one, on purpose: after an erasure the reference has to stop
        // meaning a person to everyone, including code that is probing.
        (await _registry.ResolveIdentityAsync("00000000000000000000000000000000")).Should().BeNull();
    }

    [Fact]
    public async Task EncryptedIdentity_IsBoundToItsOwnReference()
    {
        // The reference is the associated data, so a row's identity cannot be moved onto another
        // subject and still decrypt.
        var a = await _registry.GetOrCreateReferenceAsync("Customer", "ada@example.com");
        var b = await _registry.GetOrCreateReferenceAsync("Customer", "bob@example.com");
        _db.ChangeTracker.Clear();

        var rowA = await _db.Subjects.SingleAsync(s => s.SubjectRef == a);
        var rowB = await _db.Subjects.SingleAsync(s => s.SubjectRef == b);
        rowB.Identifier = rowA.Identifier;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        (await _registry.ResolveIdentityAsync(b)).Should().BeNull("a moved identity must not decrypt");
    }

    private sealed class FixedLookupKey : ISubjectLookupKeyProvider
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        public ValueTask<byte[]> GetLookupKeyAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_key);
    }
}
