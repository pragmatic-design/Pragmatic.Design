using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The commands of the plans that filter Pragmatic's entities: one per plan and combination of filter states (the plan's mask), since each
///     combination is a statement of its own (<see cref="SqlPlanFilters" />). A session sees few combinations: its caller's, and those its
///     lifts open.
/// </summary>
public sealed partial class SqlSession
{
    private Dictionary<(int Plan, ulong Mask, string? Text), DbCommand>? _filtered;

    /// <summary>
    ///     The filters of Pragmatic's entities for this session's caller: which are lifted, and the tenant, user and scopes they read
    ///     (<see cref="ISqlFilters" />). Null: every filter active, every value empty.
    /// </summary>
    public ISqlFilters? Filters { get; init; }

    /// <summary>
    ///     The command this session prepared for a dynamic plan's statement, or null the first time: by the combination of filter states, and by
    ///     the text when a predicate composed at runtime is part of it (null otherwise).
    /// </summary>
    internal DbCommand? CommandFor(int planId, ulong mask, string? text)
        => _filtered is not null && _filtered.TryGetValue((planId, mask, text), out var command) ? command : null;

    internal void Keep(int planId, ulong mask, string? text, DbCommand command) => (_filtered ??= [])[(planId, mask, text)] = command;

    private void DisposeFilteredCommands()
    {
        if (_filtered is null)
            return;
        foreach (var command in _filtered.Values)
            command.Dispose();
    }
}
