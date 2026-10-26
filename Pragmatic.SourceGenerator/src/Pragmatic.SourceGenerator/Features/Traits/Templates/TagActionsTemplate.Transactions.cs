namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     The EF Core bodies' transactions: the execution strategy a retried body runs in, the serializable transaction the per-entity limit
///     needs, and the commit that lands the set-based counter update with the pending changes.
/// </summary>
internal sealed partial class TagActionsTemplate
{
    /// <summary>
    ///     Emits the predicate that tells a lost serialization race from a genuine failure. SQLSTATE
    ///     40001 (serialization_failure) and 40P01 (deadlock_detected) are the standard codes a
    ///     database uses to say "you two collided, one of you must retry"; anything else is rethrown
    ///     rather than dressed up as a conflict.
    /// </summary>
    private void RenderSerializationConflictDetector()
    {
        AppendLine();
        XmlSummary("True when the exception is the database refusing one of two colliding transactions.");
        AppendLine("private static bool IsSerializationConflict(global::System.Exception? ex)");
        Block(() =>
        {
            AppendLine("for (var e = ex; e is not null; e = e.InnerException)");
            IncreaseIndent();
            AppendLine("if (e is global::System.Data.Common.DbException db && db.SqlState is \"40001\" or \"40P01\")");
            IncreaseIndent();
            AppendLine("return true;");
            DecreaseIndent();
            DecreaseIndent();
            AppendLine("return false;");
        });
    }

    /// <summary>
    ///     Emits <c>Execute</c> as the body run inside the context's execution strategy, and opens the
    ///     body's own method, <c>ExecuteOnceAsync</c>, for the caller to fill.
    /// </summary>
    /// <remarks>
    ///     Both bodies open a transaction of their own, and an execution strategy that retries — a generated
    ///     host configures one for every server provider — refuses a transaction opened outside
    ///     it. The whole body can run again: nothing in it reaches outside the database before the commit,
    ///     re-applying a tag the entity already carries is a no-op, and a retry starts from a cleared change
    ///     tracker so the failed attempt's link is not written twice. Inside a unit already running, the
    ///     strategy passes through and there is no retry of its own.
    /// </remarks>
    private void RenderRetriableExecute(string returnType)
    {
        AppendLine($"public override Task<{returnType}> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            AppendLine("var attempts = 0;");
            AppendLine("return _db.Database.CreateExecutionStrategy().ExecuteAsync(token =>");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("if (attempts++ > 0)");
            AppendLine("    _db.ChangeTracker.Clear();");
            AppendLine("return ExecuteOnceAsync(token);");
            DecreaseIndent();
            AppendLine("}, ct);");
        });
        AppendLine();
        AppendLine($"private async Task<{returnType}> ExecuteOnceAsync(CancellationToken ct)");
    }

    /// <summary>
    ///     Opens the transaction that makes the per-entity limit enforceable.
    ///     <para>
    ///     Counting and inserting are two statements: under READ COMMITTED two concurrent callers both
    ///     read "one below the limit" and both insert, so the limit could be crossed. Serializable is
    ///     what makes the pair indivisible — the database refuses one of them rather than letting both
    ///     through. It is scoped to the add path and only when a limit is configured, because that is
    ///     the only place where the read has to stay true until the write lands.
    ///     </para>
    /// </summary>
    private void RenderLimitTransactionOpen()
    {
        if (_model.MaxPerEntity <= 0)
            return;

        AppendLine("await using var _tx = await _db.Database.BeginTransactionAsync(");
        AppendLine("    global::System.Data.IsolationLevel.Serializable, ct).ConfigureAwait(false);");
        AppendLine();
    }

    /// <summary>
    ///     Renders the pending changes and <paramref name="renderSideEffects"/> as one transaction.
    ///     <para>
    ///     `ExecuteUpdate` runs immediately in its own implicit transaction, while entities added to the
    ///     change tracker are written by the invoker's single <c>SaveChangesAsync</c> after
    ///     <c>Execute</c> returns. Those two are not atomic with respect to each other, which is how a
    ///     usage counter could be raised for a link that was never inserted.
    ///     </para>
    /// </summary>
    /// <param name="renderSideEffects">Statements that must land with the pending changes.</param>
    /// <param name="renderAfterSave">Statements that need the save to have happened first.</param>
    private void RenderTransactionalCommit(Action renderSideEffects, Action? renderAfterSave = null)
    {
        // The add path already opened one for the limit; opening a second here would nest.
        if (!(_kind == TagActionKind.Add && _model.MaxPerEntity > 0))
            AppendLine("await using var _tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);");
        renderSideEffects();
        AppendLine("await _db.SaveChangesAsync(ct).ConfigureAwait(false);");
        if (renderAfterSave is not null)
        {
            AppendLine();
            renderAfterSave();
            AppendLine();
        }

        AppendLine("await _tx.CommitAsync(ct).ConfigureAwait(false);");
    }
}
