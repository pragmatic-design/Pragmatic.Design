using System.Text.Json;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.Json;
using Pragmatic.Temporal.Json.Behaviors;
using Pragmatic.Temporal.Testing;

namespace Pragmatic.Temporal.AspNetCore.Tests;

public class TemporalJsonModifierTests
{
    private static readonly DateTimeOffset SummerNoonUtc =
        new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static JsonSerializerOptions CreateOptions()
    {
        return new JsonSerializerOptions().AddPragmaticTemporalBehaviors();
    }

    private static IDisposable UseRomeContext()
    {
        TemporalContextHolder.Current = TestTemporalContext.ForRome(SummerNoonUtc);
        return new HolderReset();
    }

    private sealed class HolderReset : IDisposable
    {
        public void Dispose()
        {
            TemporalContextHolder.Current = null;
        }
    }

    private sealed class ToClientDto
    {
        public DateTimeOffset CreatedAt { get; set; }
    }

    [Fact]
    public void ToClientTimezone_ConvertsOutboundValueToClientZone()
    {
        TemporalJsonBehaviorRegistry.Register<ToClientDto>(
            nameof(ToClientDto.CreatedAt), TemporalJsonBehavior.ToClientTimezone);
        using var _ = UseRomeContext();

        var json = JsonSerializer.Serialize(new ToClientDto { CreatedAt = SummerNoonUtc }, CreateOptions());

        // Rome is UTC+2 in June: 12:00Z → 14:00+02:00.
        Assert.Contains("14:00:00+02:00", json);
    }

    /// <summary>The behaviour survives a naming policy, which every application has.</summary>
    /// <remarks>
    ///     ⚠️ Every other test in this file builds bare <c>JsonSerializerOptions</c>, where the name on
    ///     the wire and the name the registry is keyed by are the same string — so none of them says
    ///     what happens when they differ. The generated host configures camelCase, and this module's own
    ///     <c>CreateTemporalOptions()</c> does too, so that is the shape a caller actually has: the
    ///     registry holds <c>CreatedAt</c> and the payload says <c>createdAt</c>, and the lookup has to
    ///     hit anyway.
    /// </remarks>
    [Fact]
    public void ToClientTimezone_ConvertsEvenWhenTheWireNameIsNotTheClrName()
    {
        TemporalJsonBehaviorRegistry.Register<ToClientDto>(
            nameof(ToClientDto.CreatedAt), TemporalJsonBehavior.ToClientTimezone);
        using var _ = UseRomeContext();

        var camelCase = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            .AddPragmaticTemporalBehaviors();

        var json = JsonSerializer.Serialize(new ToClientDto { CreatedAt = SummerNoonUtc }, camelCase);

        Assert.Contains("\"createdAt\"", json);
        Assert.Contains("14:00:00+02:00", json);
    }

    private sealed class AsUtcDto
    {
        public DateTimeOffset CreatedAt { get; set; }
    }

    [Fact]
    public void AsUtc_NormalizesOutboundValueToUtc()
    {
        TemporalJsonBehaviorRegistry.Register<AsUtcDto>(
            nameof(AsUtcDto.CreatedAt), TemporalJsonBehavior.AsUtc);

        var local = new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.FromHours(2));
        var json = JsonSerializer.Serialize(new AsUtcDto { CreatedAt = local }, CreateOptions());

        Assert.Contains("12:00:00+00:00", json);
    }

    private sealed class KeepDto
    {
        public DateTimeOffset CreatedAt { get; set; }
    }

    [Fact]
    public void KeepTimezone_PassesValueThroughUnchanged()
    {
        TemporalJsonBehaviorRegistry.Register<KeepDto>(
            nameof(KeepDto.CreatedAt), TemporalJsonBehavior.KeepTimezone);
        using var _ = UseRomeContext();

        var original = new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.FromHours(-5));
        var json = JsonSerializer.Serialize(new KeepDto { CreatedAt = original }, CreateOptions());

        Assert.Contains("09:30:00-05:00", json);
    }

    private sealed class FromClientDto
    {
        public DateTime OccurredAt { get; set; }
    }

    [Fact]
    public void FromClientTimezone_InterpretsInboundWallTimeInClientZone()
    {
        TemporalJsonBehaviorRegistry.Register<FromClientDto>(
            nameof(FromClientDto.OccurredAt), TemporalJsonBehavior.FromClientTimezone);
        using var _ = UseRomeContext();

        var dto = JsonSerializer.Deserialize<FromClientDto>(
            """{"OccurredAt":"2026-06-01T14:00:00"}""", CreateOptions())!;

        // Wall 14:00 in Rome (UTC+2) → 12:00 UTC.
        Assert.Equal(DateTimeKind.Utc, dto.OccurredAt.Kind);
        Assert.Equal(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc), dto.OccurredAt);
    }

    private sealed class InboundAsUtcDto
    {
        public DateTime OccurredAt { get; set; }
    }

    [Fact]
    public void AsUtc_InterpretsInboundWallTimeAsUtc()
    {
        TemporalJsonBehaviorRegistry.Register<InboundAsUtcDto>(
            nameof(InboundAsUtcDto.OccurredAt), TemporalJsonBehavior.AsUtc);

        var dto = JsonSerializer.Deserialize<InboundAsUtcDto>(
            """{"OccurredAt":"2026-06-01T14:00:00"}""", CreateOptions())!;

        Assert.Equal(DateTimeKind.Utc, dto.OccurredAt.Kind);
        Assert.Equal(new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc), dto.OccurredAt);
    }

    private sealed class NoContextDto
    {
        public DateTimeOffset CreatedAt { get; set; }
    }

    [Fact]
    public void ToClientTimezone_WithoutAmbientContext_PassesThrough()
    {
        TemporalJsonBehaviorRegistry.Register<NoContextDto>(
            nameof(NoContextDto.CreatedAt), TemporalJsonBehavior.ToClientTimezone);

        var json = JsonSerializer.Serialize(new NoContextDto { CreatedAt = SummerNoonUtc }, CreateOptions());

        Assert.Contains("12:00:00+00:00", json);
    }

    private sealed class CamelCaseDto
    {
        public DateTimeOffset CreatedAt { get; set; }
    }

    [Fact]
    public void Registration_MatchesCamelCaseWireNames()
    {
        TemporalJsonBehaviorRegistry.Register<CamelCaseDto>(
            nameof(CamelCaseDto.CreatedAt), TemporalJsonBehavior.AsUtc);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            .AddPragmaticTemporalBehaviors();
        var local = new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.FromHours(2));

        var json = JsonSerializer.Serialize(new CamelCaseDto { CreatedAt = local }, options);

        Assert.Contains("\"createdAt\":\"2026-06-01T12:00:00+00:00\"", json);
    }

    private sealed class UnregisteredDto
    {
        public DateTimeOffset CreatedAt { get; set; }
    }

    [Fact]
    public void UnregisteredType_IsUntouched()
    {
        using var _ = UseRomeContext();
        var original = new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.FromHours(-5));

        var json = JsonSerializer.Serialize(new UnregisteredDto { CreatedAt = original }, CreateOptions());

        Assert.Contains("09:30:00-05:00", json);
    }
}
