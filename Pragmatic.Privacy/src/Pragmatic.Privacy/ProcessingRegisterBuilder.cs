using Microsoft.Extensions.Options;

namespace Pragmatic.Privacy;

/// <summary>
///     Joins what the code declares to what the controller declares, producing the Article 30 register.
/// </summary>
public sealed class ProcessingRegisterBuilder(
    IEnumerable<IProcessingActivitySource> sources,
    IOptions<ProcessingRegisterOptions> options,
    TimeProvider timeProvider) : IProcessingRegisterBuilder
{
    /// <summary>Builds the register as it stands right now.</summary>
    public async ValueTask<ProcessingRegister> BuildAsync(CancellationToken ct = default)
    {
        var settings = options.Value;
        var activities = new List<ProcessingActivity>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sources)
        foreach (var activity in await source.GetActivitiesAsync(ct).ConfigureAwait(false))
        {
            // The same type can be reported by more than one assembly's metadata when it is shared.
            // Keeping the first is right — they describe the same declarations — but counting it twice
            // would inflate the register and suggest processing that does not happen.
            if (!seen.Add(activity.EntityType))
                continue;

            activities.Add(settings.Purposes.TryGetValue(activity.EntityType, out var purpose)
                ? activity with { Purpose = purpose }
                : activity);
        }

        // Sorted so two builds of the same system produce the same document. A register that reorders
        // itself cannot be diffed, and a register nobody can diff is one nobody reviews.
        activities.Sort(static (a, b) => string.CompareOrdinal(a.EntityType, b.EntityType));

        var operations = await CollectOperationsAsync(settings, ct).ConfigureAwait(false);

        return new ProcessingRegister(
            settings.ControllerName,
            settings.ControllerContact,
            timeProvider.GetUtcNow(),
            activities,
            operations);
    }

    /// <summary>The operations every source reports, deduplicated and joined to their declared purposes.</summary>
    /// <remarks>
    ///     Keyed by operation <b>and</b> entity. By operation, because two operations on the same entity are
    ///     two entries, which is the whole reason the operation half exists; by entity too, because one
    ///     operation touching two entities is reported once per entity, and keying on the operation alone
    ///     kept the first and dropped the rest — the register understated exactly the operations that
    ///     process most.
    /// </remarks>
    private async ValueTask<IReadOnlyList<ProcessingOperation>> CollectOperationsAsync(
        ProcessingRegisterOptions settings, CancellationToken ct)
    {
        var operations = new List<ProcessingOperation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sources)
        foreach (var operation in await source.GetOperationsAsync(ct).ConfigureAwait(false))
        {
            if (!seen.Add(operation.OperationType + "\n" + operation.EntityType))
                continue;

            operations.Add(settings.OperationPurposes.TryGetValue(operation.OperationType, out var purpose)
                ? operation with { Purpose = purpose }
                : operation);
        }

        operations.Sort(static (a, b) =>
        {
            var byOperation = string.CompareOrdinal(a.OperationType, b.OperationType);
            return byOperation != 0 ? byOperation : string.CompareOrdinal(a.EntityType, b.EntityType);
        });

        return operations;
    }
}
