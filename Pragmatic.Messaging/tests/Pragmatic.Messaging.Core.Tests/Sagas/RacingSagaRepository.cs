using System.Text.Json;
using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Core.Tests.Sagas;

/// <summary>
///     One saga's state held as a document with a version, like a row — and a rival writer that, when armed,
///     saves first: the save it interrupts was based on a version that is no longer there, and loses.
/// </summary>
/// <remarks>
///     The race is choreographed, not hoped for: the rival's write happens inside the losing save, at the
///     moment two concurrent handlers would collide, every time.
/// </remarks>
public sealed class RacingSagaRepository : ISagaRepository<TwoAcknowledgementsSaga>
{
    private string? _document;
    private long _version;
    private long _versionRead;

    /// <summary>Applied by the rival before the next save(s) compare versions; null when disarmed.</summary>
    public Action<TwoAcknowledgementsSaga>? Rival { get; set; }

    /// <summary>How many saves the rival interrupts before it stops; the default is the first one only.</summary>
    public int RivalSaves { get; set; } = 1;

    /// <summary>Whether the orchestrator marked the saga faulted.</summary>
    public bool Faulted { get; private set; }

    /// <summary>How many saves were attempted after the start.</summary>
    public int SaveAttempts { get; private set; }

    /// <summary>The state as stored.</summary>
    public TwoAcknowledgementsSaga Stored => Deserialize(_document!);

    public Task<TwoAcknowledgementsSaga?> FindByCorrelationAsync(string correlationId, CancellationToken ct = default)
    {
        if (_document is null)
            return Task.FromResult<TwoAcknowledgementsSaga?>(null);

        _versionRead = _version;
        return Task.FromResult<TwoAcknowledgementsSaga?>(Deserialize(_document));
    }

    public Task<TwoAcknowledgementsSaga?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => FindByCorrelationAsync("", ct);

    public Task SaveAsync(TwoAcknowledgementsSaga saga, CancellationToken ct = default)
    {
        if (_document is not null)
        {
            SaveAttempts++;
            if (Rival is { } rival && RivalSaves-- > 0)
            {
                var theirs = Deserialize(_document);
                rival(theirs);
                _document = JsonSerializer.Serialize(theirs);
                _version++;
            }

            if (_versionRead != _version)
                throw new SagaConcurrencyException(saga.Id, saga.CorrelationId);
        }

        _document = JsonSerializer.Serialize(saga);
        _version++;
        _versionRead = _version;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TwoAcknowledgementsSaga>> GetActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TwoAcknowledgementsSaga>>([]);

    public Task SetTimeoutAsync(Guid sagaId, DateTimeOffset? timeoutAt, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<TwoAcknowledgementsSaga>> GetDueForTimeoutAsync(DateTimeOffset asOf, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TwoAcknowledgementsSaga>>([]);

    public Task MarkTimedOutAsync(Guid sagaId, CancellationToken ct = default) => Task.CompletedTask;

    public Task MarkFaultedAsync(Guid sagaId, string error, CancellationToken ct = default)
    {
        Faulted = true;
        return Task.CompletedTask;
    }

    private static TwoAcknowledgementsSaga Deserialize(string document)
        => JsonSerializer.Deserialize<TwoAcknowledgementsSaga>(document)!;
}
