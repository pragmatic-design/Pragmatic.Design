using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Cryptography.EFCore.Tests;

/// <summary>
///     Covers the per-subject key lifecycle: creation on first use, stability across instances,
///     resolution by the key id a ciphertext carries, and destruction — including the invariant that a
///     destroyed subject is never resurrected.
/// </summary>
public sealed class EfCoreSubjectKeyStoreTests : IDisposable
{
    private readonly SubjectKeyStoreFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task GetOrCreate_FirstCall_CreatesA256BitKey()
    {
        var key = await _fixture.Store.GetOrCreateAsync("subject-a");

        key.Material.Should().HaveCount(32);
        key.KeyId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetOrCreate_SecondCall_ReturnsTheSameKey()
    {
        var first = await _fixture.Store.GetOrCreateAsync("subject-a");
        var second = await _fixture.Store.GetOrCreateAsync("subject-a");

        second.KeyId.Should().Be(first.KeyId);
        second.Material.Should().Equal(first.Material);
    }

    [Fact]
    public async Task GetOrCreate_DifferentSubjects_GetDifferentKeys()
    {
        var a = await _fixture.Store.GetOrCreateAsync("subject-a");
        var b = await _fixture.Store.GetOrCreateAsync("subject-b");

        b.KeyId.Should().NotBe(a.KeyId);
    }

    [Fact]
    public async Task GetOrCreate_SurvivesANewStoreOverTheSameDatabase()
    {
        var created = await _fixture.Store.GetOrCreateAsync("subject-a");

        var reloaded = await _fixture.NewStoreOverSameDatabase().GetOrCreateAsync("subject-a");

        reloaded.Material.Should().Equal(created.Material);
    }

    [Fact]
    public async Task GetOrCreate_StoresTheKeyWrapped_NotInTheClear()
    {
        var key = await _fixture.Store.GetOrCreateAsync("subject-a");

        var row = await _fixture.Db.SubjectKeys.SingleAsync(r => r.SubjectRef == "subject-a");

        row.WrappedKey.Should().NotBeNull();
        Contains(row.WrappedKey!, key.Material)
            .Should().BeFalse("the raw key must not appear anywhere in the stored row");
    }

    [Fact]
    public async Task Unwrap_RowWhoseKeyIdDoesNotMatchItsMaterial_Throws()
    {
        // The id is a fingerprint of the material, so reads that resolve by id depend on the two
        // agreeing. Swap in a different key — validly wrapped for this same subject, so the AAD check
        // still passes — and the mismatch must be caught rather than silently handing out a key whose
        // id is not the one the row advertises.
        await _fixture.Store.GetOrCreateAsync("subject-a");

        var otherMaterial = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var row = await _fixture.Db.SubjectKeys.SingleAsync(r => r.SubjectRef == "subject-a");
        row.WrappedKey = _fixture.Master.EncryptBytes(otherMaterial, "subject-a");
        await _fixture.Db.SaveChangesAsync();
        _fixture.Db.ChangeTracker.Clear();

        var act = async () => await _fixture.Store.GetOrCreateAsync("subject-a");

        await act.Should().ThrowAsync<System.Security.Cryptography.CryptographicException>()
            .WithMessage("*inconsistent*");
    }

    /// <summary>True when <paramref name="needle"/> appears as a contiguous run inside <paramref name="haystack"/>.</summary>
    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return true;

        return false;
    }

    // =========================================================================
    // Resolution by key id — what the decryption path actually does
    // =========================================================================

    [Fact]
    public async Task FindById_LiveKey_ResolvesIt()
    {
        var created = await _fixture.Store.GetOrCreateAsync("subject-a");

        var resolved = await _fixture.Store.FindByIdAsync(created.KeyId);

        resolved.Should().NotBeNull();
        resolved!.Material.Should().Equal(created.Material);
    }

    [Fact]
    public async Task FindById_UnknownKey_ReturnsNull()
    {
        var resolved = await _fixture.Store.FindByIdAsync("deadbeef");

        resolved.Should().BeNull();
    }

    // =========================================================================
    // Destruction
    // =========================================================================

    [Fact]
    public async Task Destroy_ReportsTheKeyIdAndTime()
    {
        var created = await _fixture.Store.GetOrCreateAsync("subject-a");

        var report = await _fixture.Store.DestroyAsync("subject-a");

        report.SubjectRef.Should().Be("subject-a");
        report.KeyId.Should().Be(created.KeyId);
        report.AlreadyDestroyed.Should().BeFalse();
    }

    [Fact]
    public async Task Destroy_KeepsTheRowAndTheKeyId_ButDropsTheKey()
    {
        var created = await _fixture.Store.GetOrCreateAsync("subject-a");

        await _fixture.Store.DestroyAsync("subject-a");

        var row = await _fixture.Db.SubjectKeys.SingleAsync(r => r.SubjectRef == "subject-a");
        row.WrappedKey.Should().BeNull("the key must be gone");
        row.KeyId.Should().Be(created.KeyId, "the id must remain, or erased data becomes indistinguishable from tampered");
        row.DestroyedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Destroy_Twice_IsIdempotentAndKeepsTheOriginalTime()
    {
        await _fixture.Store.GetOrCreateAsync("subject-a");
        var first = await _fixture.Store.DestroyAsync("subject-a");

        var second = await _fixture.Store.DestroyAsync("subject-a");

        second.AlreadyDestroyed.Should().BeTrue();
        second.DestroyedAt.Should().Be(first.DestroyedAt);
    }

    [Fact]
    public async Task Destroy_UnknownSubject_Throws()
    {
        // Reporting an erasure that never happened would be worse than failing.
        var act = async () => await _fixture.Store.DestroyAsync("never-existed");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FindById_AfterDestruction_ReturnsNull()
    {
        var created = await _fixture.Store.GetOrCreateAsync("subject-a");
        await _fixture.Store.DestroyAsync("subject-a");

        var resolved = await _fixture.Store.FindByIdAsync(created.KeyId);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreate_AfterDestruction_ThrowsAndDoesNotResurrect()
    {
        // The invariant the whole erasure story rests on: if the key came back, data written
        // afterwards would be readable under a reference already reported as erased.
        await _fixture.Store.GetOrCreateAsync("subject-a");
        await _fixture.Store.DestroyAsync("subject-a");

        var act = async () => await _fixture.Store.GetOrCreateAsync("subject-a");

        await act.Should().ThrowAsync<SubjectKeyDestroyedException>();

        var row = await _fixture.Db.SubjectKeys.SingleAsync(r => r.SubjectRef == "subject-a");
        row.WrappedKey.Should().BeNull("a failed resurrection must not have re-wrapped anything");
    }

    // =========================================================================
    // The wrapped key is bound to its subject
    // =========================================================================

    [Fact]
    public async Task WrappedKey_MovedToAnotherSubject_FailsToUnwrap()
    {
        // Associated data binds the wrapped key to its subject reference. Without it, an operator with
        // write access to the table could hand subject B the key of subject A.
        await _fixture.Store.GetOrCreateAsync("subject-a");
        var b = await _fixture.Store.GetOrCreateAsync("subject-b");

        var rowA = await _fixture.Db.SubjectKeys.SingleAsync(r => r.SubjectRef == "subject-a");
        var rowB = await _fixture.Db.SubjectKeys.SingleAsync(r => r.SubjectRef == "subject-b");
        rowB.WrappedKey = rowA.WrappedKey;
        await _fixture.Db.SaveChangesAsync();
        _fixture.Db.ChangeTracker.Clear();

        var act = async () => await _fixture.Store.FindByIdAsync(b.KeyId);

        await act.Should().ThrowAsync<System.Security.Cryptography.CryptographicException>();
    }
}
