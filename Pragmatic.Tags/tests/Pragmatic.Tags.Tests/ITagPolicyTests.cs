namespace Pragmatic.Tags.Tests;

public class ITagPolicyTests
{
    /// <summary>
    /// Implementor that overrides nothing — exercises the default interface methods.
    /// Default interface members are only dispatched through the interface reference.
    /// </summary>
    private sealed class DefaultPolicy : ITagPolicy<Guid>
    {
    }

    private static ITagPolicy<Guid> Sut() => new DefaultPolicy();

    // ── IsAllowedAsync default ──────────────────────────────────────────

    [Theory]
    [InlineData("urgent")]
    [InlineData("a")]
    [InlineData("  has surrounding space  ")]
    public async Task IsAllowedAsync_NonEmptyValue_ReturnsTrue(string value)
    {
        var allowed = await Sut().IsAllowedAsync(value);

        allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task IsAllowedAsync_EmptyOrWhitespace_ReturnsFalse(string value)
    {
        var allowed = await Sut().IsAllowedAsync(value);

        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task IsAllowedAsync_NullValue_ReturnsFalse()
    {
        var allowed = await Sut().IsAllowedAsync(null!);

        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task IsAllowedAsync_RespectsCancellationTokenParameter_WithoutThrowing()
    {
        using var cts = new CancellationTokenSource();

        var allowed = await Sut().IsAllowedAsync("ok", cts.Token);

        allowed.Should().BeTrue();
    }

    // ── Normalize default ───────────────────────────────────────────────

    [Theory]
    [InlineData("Urgent", "urgent")]
    [InlineData("  Mixed Case  ", "mixed case")]
    [InlineData("ALREADY", "already")]
    [InlineData("already-lower", "already-lower")]
    public void Normalize_TrimsAndLowercases(string input, string expected)
    {
        Sut().Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_EmptyString_ReturnsEmptyString()
    {
        Sut().Normalize(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Normalize_WhitespaceOnly_ReturnsEmptyString()
    {
        Sut().Normalize("   ").Should().BeEmpty();
    }

    [Fact]
    public void Normalize_UsesInvariantCulture()
    {
        // The default implementation uses ToLowerInvariant, so 'PUBLIC' maps to
        // 'public' regardless of the ambient culture this test runs under.
        Sut().Normalize("PUBLIC").Should().Be("public");
    }

    // ── Lifecycle hook defaults ─────────────────────────────────────────

    [Fact]
    public async Task OnTagAddedAsync_Default_CompletesSuccessfully()
    {
        var task = Sut().OnTagAddedAsync(Guid.NewGuid(), Guid.NewGuid(), "urgent");

        await task;
        task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task OnTagRemovedAsync_Default_CompletesSuccessfully()
    {
        var task = Sut().OnTagRemovedAsync(Guid.NewGuid(), Guid.NewGuid(), "urgent");

        await task;
        task.IsCompletedSuccessfully.Should().BeTrue();
    }

    // ── Override support ────────────────────────────────────────────────

    private sealed class RejectingPolicy : ITagPolicy<int>
    {
        public bool AddInvoked { get; private set; }

        public Task<bool> IsAllowedAsync(string tagValue, CancellationToken ct = default)
            => Task.FromResult(false);

        public string Normalize(string tagValue) => tagValue.ToUpperInvariant();

        public Task OnTagAddedAsync(int entityId, Guid tagId, string tagValue, CancellationToken ct = default)
        {
            AddInvoked = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task IsAllowedAsync_CustomOverride_ReplacesDefaultBehavior()
    {
        ITagPolicy<int> policy = new RejectingPolicy();

        var allowed = await policy.IsAllowedAsync("normally-valid");

        allowed.Should().BeFalse();
    }

    [Fact]
    public void Normalize_CustomOverride_ReplacesDefaultBehavior()
    {
        ITagPolicy<int> policy = new RejectingPolicy();

        policy.Normalize("lower").Should().Be("LOWER");
    }

    [Fact]
    public async Task OnTagAddedAsync_CustomOverride_IsInvoked()
    {
        var policy = new RejectingPolicy();

        await policy.OnTagAddedAsync(1, Guid.NewGuid(), "x");

        policy.AddInvoked.Should().BeTrue();
    }
}
