using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Xunit;

namespace Pragmatic.Testing.Tests.Mocking;

/// <summary>
///     The shape the generator will emit, written by hand first. It fixes the contract the templates
///     have to produce, and proves the mechanism works before any generator exists: explicit
///     interface implementation, so the public member can keep the interface member's own name and
///     expose <c>Returns</c>/<c>Received</c> while the system under test goes through the interface.
/// </summary>
public sealed class HandAuthoredMockSpikeTests
{
    /// <summary>Stands in for <c>Pragmatic.Temporal.Clock.IClock</c>, without taking the dependency.</summary>
    private interface IClockLike
    {
        DateTimeOffset UtcNow { get; }
        DateOnly UtcToday { get; }
        TimeProvider GetTimeProvider();
    }

    /// <summary>Stands in for a store with an async member and a void one.</summary>
    private interface IStoreLike
    {
        ValueTask<string?> GetAsync(string key, CancellationToken ct);
        void Add(object entity);
    }

    // ─── what the generator will emit ───────────────────────────────────────

    private sealed class ClockLikeMock : IClockLike
    {
        public MockProperty<DateTimeOffset> UtcNow { get; } = new("IClockLike.UtcNow");
        public MockProperty<DateOnly> UtcToday { get; } = new("IClockLike.UtcToday");
        public MockMethod<TimeProvider> GetTimeProvider { get; } = new("IClockLike.GetTimeProvider");

        DateTimeOffset IClockLike.UtcNow => UtcNow.Get();
        DateOnly IClockLike.UtcToday => UtcToday.Get();
        TimeProvider IClockLike.GetTimeProvider() => GetTimeProvider.Invoke();
    }

    private sealed class StoreLikeMock : IStoreLike
    {
        public MockMethod<string, CancellationToken, ValueTask<string?>> GetAsync { get; } =
            new("IStoreLike.GetAsync");

        public MockVoidMethod<object> Add { get; } = new("IStoreLike.Add");

        ValueTask<string?> IStoreLike.GetAsync(string key, CancellationToken ct) => GetAsync.Invoke(key, ct);
        void IStoreLike.Add(object entity) => Add.Invoke(entity);
    }

    // ─── the system under test, which only ever sees the interface ──────────

    private static string Describe(IClockLike clock) =>
        $"{clock.UtcNow:yyyy-MM-dd} / {clock.UtcToday:yyyy-MM-dd}";

    // ─── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public void ConfiguredProperty_IsSeenThroughTheInterface()
    {
        var clock = new ClockLikeMock();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero));
        clock.UtcToday.Returns(new DateOnly(2026, 8, 3));

        Describe(clock).Should().Be("2026-08-03 / 2026-08-03");

        clock.UtcNow.Received(1);
        clock.UtcToday.Received(1);
        clock.GetTimeProvider.DidNotReceive();
    }

    [Fact]
    public async Task AsyncMethod_ReturnsConfiguredValueAndRecordsArguments()
    {
        var store = new StoreLikeMock();
        store.GetAsync.Returns(new ValueTask<string?>("value"));

        var result = await ((IStoreLike)store).GetAsync("k", CancellationToken.None);

        result.Should().Be("value");
        store.GetAsync.Received(1, "k");
        store.GetAsync.DidNotReceive("other");
    }

    [Fact]
    public async Task AsyncMethod_FactorySeesTheArgument()
    {
        var store = new StoreLikeMock();
        store.GetAsync.Returns((key, _) => new ValueTask<string?>($"v:{key}"));

        (await ((IStoreLike)store).GetAsync("a", default)).Should().Be("v:a");
        (await ((IStoreLike)store).GetAsync("b", default)).Should().Be("v:b");
    }

    [Fact]
    public void VoidMethod_IsRecordedThroughTheInterface()
    {
        var store = new StoreLikeMock();
        var entity = new object();

        ((IStoreLike)store).Add(entity);

        store.Add.Received(1, entity);
    }

    // Deliberately unmet expectations: the mock must fail, or none of the above proves anything.
    [Fact]
    public void UnmetExpectation_FailsAndNamesTheMember()
    {
        var clock = new ClockLikeMock();

        clock.Invoking(c => c.UtcNow.Received(1))
            .Should().Throw<PragmaticTestAssertionException>()
            .WithMessage("*IClockLike.UtcNow*");
    }

    [Fact]
    public void UnconfiguredMember_ReturnsDefaultRatherThanThrowing()
    {
        var clock = new ClockLikeMock();

        ((IClockLike)clock).UtcNow.Should().Be(default(DateTimeOffset));
        ((IClockLike)clock).GetTimeProvider().Should().BeNull();
    }
}
