using Pragmatic.SourceGenerator.Features.Traits.Transforms;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     The parts of an attachment the Sql or documents engine persists: the child's repository and the boundary's unit of work instead of EF
///     Core's <c>DbContext</c>. A read by key goes through the engine's filters, so the soft delete and the parent's visibility and tenancy hold
///     without being restated. The upload keeps EF Core's order (the file stored, the row committed, the file taken back out when the row does
///     not commit); with a per-entity limit it counts and inserts in one unit of the unit of work, in a serializable transaction on the Sql engine
///     as with EF Core, and on the documents engine the count before the write is best effort, as the tags' limit is (owner's decision of
///     2026-10-26).
/// </summary>
internal sealed partial class AttachmentActionsTemplate
{
    private const string Spec = "global::Pragmatic.Specification.Spec";

    private void RenderEngineFields()
    {
        AppendLine($"private global::Pragmatic.Persistence.Repository.IRepository<{_model.AttachmentTypeName}> _attachments = null!;");
        // The upload commits its row itself, so a failed commit can take the stored file back out.
        if (_kind == AttachmentActionKind.Upload)
            AppendLine("private global::Pragmatic.Persistence.Repository.IUnitOfWork _unitOfWork = null!;");
    }

    /// <summary>The attachments of the parent in the route, counted by the engine.</summary>
    private string EngineCount(string ct)
        => $"_attachments.CountAsync({Spec}<{_model.AttachmentTypeName}>.Where(a => a.{_model.ParentFkPropertyName} == {_model.ParentFkPropertyName}), {ct}).ConfigureAwait(false)";

    /// <summary>
    ///     The attachment by key, as <paramref name="variable" />, or not found: object-level auth, the attachment belongs to the parent in the
    ///     route and not only to the id.
    /// </summary>
    private void RenderEngineFind(string variable, string resultType)
    {
        AppendLine($"var {variable} = await _attachments.GetByIdAsync(AttachmentId, ct).ConfigureAwait(false);");
        AppendLine($"if ({variable} is null || {variable}.{_model.ParentFkPropertyName} != {_model.ParentFkPropertyName})");
        IncreaseIndent();
        AppendLine($"return {resultType}.Failure(NotFoundError.For(\"{_model.AttachmentTypeName}\", AttachmentId.ToString()));");
        DecreaseIndent();
    }

    private void RenderEngineGetByIdBody()
    {
        var dtoName = $"{_model.AttachmentTypeName}Dto";
        RenderEngineFind("attachment", $"Result<{dtoName}, IError>");
        AppendLine($"return new {dtoName}");
        AppendLine("{");
        IncreaseIndent();
        foreach (var property in TraitDtoShape.Attachment(_model))
            AppendLine($"{property.Name} = attachment.{property.Source},");
        DecreaseIndent();
        AppendLine("};");
    }

    /// <summary>The count and the insert as one unit of the unit of work, run again from a cleared tracker on a transient failure.</summary>
    private void RenderEngineLimitedInsert()
    {
        AppendLine("var withinLimit = await _unitOfWork.ExecuteAsync(token => InsertWithinLimitAsync(attachment, token), ct).ConfigureAwait(false);");
        AppendLine();
        AppendLine("if (!withinLimit)");
        Block(() =>
        {
            RenderDeleteThumbnailIfWritten();
            AppendLine("await _storage.DeleteAsync(storageUri, ct).ConfigureAwait(false);");
            AppendLine($"return Result<Guid, IError>.Failure(new ForbiddenError {{ Resource = \"Attachment limit ({_model.MaxPerEntity})\" }});");
        });
    }

    /// <summary>
    ///     Whether the row is there. The engine's filters stay on: the caller just wrote it to a parent it may write to, so it is in what they
    ///     read, and nothing here lifts what the caller is not allowed to see.
    /// </summary>
    private void RenderEngineCommitted()
        => AppendLine($"=> await _attachments.ExistsAsync({Spec}<{_model.AttachmentTypeName}>.Where(a => a.PersistenceId == id), ct).ConfigureAwait(false);");

    private void RenderEngineLimitHelpers()
    {
        XmlSummary(_serializable
            ? "Counts and inserts in a serializable transaction: two concurrent uploads cannot both read one below the limit."
            : "Counts and inserts: best effort, two concurrent uploads can both read one below the limit.");
        AppendLine($"private async Task<bool> InsertWithinLimitAsync({_model.AttachmentTypeName} attachment, CancellationToken ct)");
        Block(() =>
        {
            if (_serializable)
                AppendLine("using var _tx = ((global::Pragmatic.Persistence.Sql.SqlUnitOfWork)_unitOfWork).Session.BeginTransaction(global::System.Data.IsolationLevel.Serializable);");
            AppendLine($"if (await {EngineCount("ct")} >= {_model.MaxPerEntity})");
            IncreaseIndent();
            AppendLine("return false;");
            DecreaseIndent();
            AppendLine();
            AppendLine("// Added again on a retry: the unit of work forgot it with the failed attempt.");
            AppendLine("_attachments.Add(attachment);");
            AppendLine("await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);");
            if (_serializable)
                AppendLine("_tx.Commit();");
            AppendLine("return true;");
        });
        AppendLine();

        // Oracle's serialization failure carries no SQLSTATE: it is read off the message, as SqlTransactionOptions reads it.
        XmlSummary("True when the exception is the database refusing one of two colliding transactions.");
        AppendLine("private static bool IsSerializationConflict(global::System.Exception? ex)");
        Block(() =>
        {
            AppendLine("for (var e = ex; e is not null; e = e.InnerException)");
            IncreaseIndent();
            AppendLine("if (e is global::System.Data.Common.DbException db && (db.SqlState is \"40001\" or \"40P01\" || db.Message.Contains(\"ORA-08177\")))");
            IncreaseIndent();
            AppendLine("return true;");
            DecreaseIndent();
            DecreaseIndent();
            AppendLine("return false;");
        });
        AppendLine();
    }
}
