using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Cryptography.EFCore.Tests;

/// <summary>
///     Covers the three read outcomes. The invariant under test is that erasure and tampering never
///     collapse into each other: if they did, either every erased record would look like an attack, or a
///     real attack would vanish into the noise of ordinary erasures.
/// </summary>
public sealed class SubjectDataProtectorTests : IDisposable
{
    private readonly SubjectKeyStoreFixture _fixture = new();
    private readonly SubjectDataProtector _protector;

    public SubjectDataProtectorTests()
        => _protector = new SubjectDataProtector(_fixture.Store, _fixture.Store);

    public void Dispose() => _fixture.Dispose();

    private static byte[] Bytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task Read_BeforeDestruction_Succeeds()
    {
        var packed = await _protector.ProtectAsync("subject-a", Bytes("personal"));

        var result = await _protector.TryReadAsync(packed);

        result.Outcome.Should().Be(DecryptOutcome.Success);
        result.Plain.Should().Equal(Bytes("personal"));
    }

    [Fact]
    public async Task Read_AfterDestruction_ReportsKeyDestroyed_NotAFailure()
    {
        var packed = await _protector.ProtectAsync("subject-a", Bytes("personal"));

        await _fixture.Store.DestroyAsync("subject-a");

        var result = await _protector.TryReadAsync(packed);

        result.Outcome.Should().Be(DecryptOutcome.KeyDestroyed);
        result.Plain.Should().BeEmpty();
    }

    [Fact]
    public async Task Read_TamperedValue_ReportsAuthenticationFailed_NeverKeyDestroyed()
    {
        // The whole point of the three-state result: a live key with a broken tag is an attack signal,
        // and must not be reported the way an erasure is.
        var packed = await _protector.ProtectAsync("subject-a", Bytes("personal"));
        packed[^1] ^= 0xFF;

        var result = await _protector.TryReadAsync(packed);

        result.Outcome.Should().Be(DecryptOutcome.AuthenticationFailed);
        result.Outcome.Should().NotBe(DecryptOutcome.KeyDestroyed);
    }

    [Fact]
    public async Task Read_TamperedValue_AfterThatSubjectWasErased_StillDistinguishesTheTwo()
    {
        // Erasing one subject must not turn another subject's tampered value into a benign "erased".
        var packedA = await _protector.ProtectAsync("subject-a", Bytes("a"));
        var packedB = await _protector.ProtectAsync("subject-b", Bytes("b"));
        await _fixture.Store.DestroyAsync("subject-a");
        packedB[^1] ^= 0xFF;

        var erased = await _protector.TryReadAsync(packedA);
        var tampered = await _protector.TryReadAsync(packedB);

        erased.Outcome.Should().Be(DecryptOutcome.KeyDestroyed);
        tampered.Outcome.Should().Be(DecryptOutcome.AuthenticationFailed);
    }

    [Fact]
    public async Task Read_UnknownKeyId_ReportsAuthenticationFailed()
    {
        // A key id this deployment never issued is not an erasure — there is nothing to say it was ever
        // ours — so it is an authentication failure.
        var packed = await _protector.ProtectAsync("subject-a", Bytes("personal"));
        await _fixture.Db.SubjectKeys.ExecuteDeleteAsync();
        _fixture.Db.ChangeTracker.Clear();

        var result = await _protector.TryReadAsync(packed);

        result.Outcome.Should().Be(DecryptOutcome.AuthenticationFailed);
    }

    [Fact]
    public async Task Read_ValueWithNoHeader_ReportsAuthenticationFailed()
    {
        var result = await _protector.TryReadAsync([1, 2, 3, 4, 5]);

        result.Outcome.Should().Be(DecryptOutcome.AuthenticationFailed);
    }

    [Fact]
    public async Task Read_WrongAssociatedData_ReportsAuthenticationFailed()
    {
        var packed = await _protector.ProtectAsync("subject-a", Bytes("personal"), associatedData: "row-1");

        var result = await _protector.TryReadAsync(packed, associatedData: "row-2");

        result.Outcome.Should().Be(DecryptOutcome.AuthenticationFailed);
    }

    [Fact]
    public async Task Protect_AfterDestruction_Throws()
    {
        await _protector.ProtectAsync("subject-a", Bytes("personal"));
        await _fixture.Store.DestroyAsync("subject-a");

        var act = async () => await _protector.ProtectAsync("subject-a", Bytes("more"));

        await act.Should().ThrowAsync<SubjectKeyDestroyedException>();
    }

    [Fact]
    public async Task Protect_TwoSubjects_ProducesValuesOnlyTheirOwnKeyCanRead()
    {
        var packedA = await _protector.ProtectAsync("subject-a", Bytes("a-secret"));
        await _protector.ProtectAsync("subject-b", Bytes("b-secret"));

        await _fixture.Store.DestroyAsync("subject-b");

        // Erasing B must leave A readable — keys are per subject, not per deployment.
        var result = await _protector.TryReadAsync(packedA);

        result.Outcome.Should().Be(DecryptOutcome.Success);
        result.Plain.Should().Equal(Bytes("a-secret"));
    }
}
