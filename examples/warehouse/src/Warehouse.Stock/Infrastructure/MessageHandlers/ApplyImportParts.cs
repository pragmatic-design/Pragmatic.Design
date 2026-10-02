using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Messaging.Batch;
using Pragmatic.Persistence.Repository;
using Warehouse.Stock.Imports.Messages;

namespace Warehouse.Stock.Infrastructure.MessageHandlers;

/// <summary>
///     A part of a supplier's file arrived: its rows are applied, by whichever Stock instance took it.
/// </summary>
/// <remarks>
///     <para>
///         The batch counts the part when this returns (<c>BatchProgressMiddleware</c>). A part with refused
///         rows is <b>failed</b>, through <see cref="BatchItemOutcome" /> and not by throwing: its valid
///         rows are applied and stay applied, and a throw would retry the part and roll them back.
///     </para>
///     <para>
///         ⚠️ <b>Parts meet.</b> Two instances apply two parts at once, and parts of one file receive into
///         the same levels. The part is one transaction, so a meeting fails it whole: a level both saved
///         (<c>[ConcurrencyAware]</c>), a level both created, or the same part delivered twice. It is
///         applied again, from the start, in a fresh scope, and the second attempt reads what the first
///         writer committed. Not left to the broker: a failed handler is nacked without requeue, and
///         without a dead-letter exchange the part would be gone.
///     </para>
///     <para>
///         A part delivered twice is applied once (<c>ApplyImportPartAction</c>): the repeat is logged and
///         counted with the outcome the part had.
///     </para>
/// </remarks>
[MessageHandler]
internal sealed partial class ApplyImportParts(
    IServiceScopeFactory scopes,
    BatchItemOutcome outcome,
    ILogger<ApplyImportParts> logger) : IMessageHandler<ImportFilePart>
{
    /// <summary>How many times a part is applied before its conflicts are given up on.</summary>
    private const int Attempts = 10;

    public async Task HandleAsync(ImportFilePart message, MessageContext context, CancellationToken ct = default)
    {
        // The import is the batch the part was dispatched in. A part with no batch headers belongs to no
        // import, and there is nothing to record it against.
        if (!BatchTracker.TryGetBatchId(context, out var importId))
            throw new InvalidOperationException($"Part {message.PartIndex} arrived without the batch it belongs to.");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var applied = await ApplyAsync(importId, message, ct).ConfigureAwait(false);
                if (applied.AlreadyApplied)
                    LogAlreadyApplied(message.PartIndex, importId);
                if (applied.Rejected > 0)
                    outcome.Fail();
                return;
            }
            catch (Exception ex) when (attempt < Attempts
                && ex is DbUpdateConcurrencyException or PersistenceRuleViolationException)
            {
                LogPartsMet(message.PartIndex, importId, attempt, ex.GetType().Name);
            }
        }
    }

    /// <summary>One attempt, in a scope of its own: a failed save leaves its DbContext unusable.</summary>
    private async Task<Dtos.ImportPartOutcomeDto> ApplyAsync(Guid importId, ImportFilePart message, CancellationToken ct)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var imports = scope.ServiceProvider.GetRequiredService<IStockImportsInternalActions>();
            var applied = await imports
                .ApplyImportPart(importId: importId, partIndex: message.PartIndex, rows: message.Rows, ct: ct)
                .ConfigureAwait(false);

            return applied.IsSuccess
                ? applied.Value
                : throw new InvalidOperationException(
                    $"Part {message.PartIndex} of import {importId} could not be applied: {applied.Error}");
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Part {PartIndex} of import {ImportId} was already applied: nothing was written.")]
    private partial void LogAlreadyApplied(int partIndex, Guid importId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Part {PartIndex} of import {ImportId} met another writer (attempt {Attempt}, {Conflict}): applying it again.")]
    private partial void LogPartsMet(int partIndex, Guid importId, int attempt, string conflict);
}
