namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     The bodies of tags the Sql or documents engine persists: the tag's and the link's repositories instead of EF Core's <c>DbContext</c>, one
///     body for both engines. The shared tag is <c>[ConcurrencyAware]</c>: its usage count is raised and lowered on the tracked row, and a save
///     that finds the row moved since the read is refused, so the body reads again (three attempts); the unique indexes refuse a tag or a link
///     minted twice by two callers, which reads again too. A tag nobody links any more is removed on its read version, so a caller who linked it
///     meanwhile keeps it. On the Sql engine with a per-entity limit the add runs in a serializable transaction of the session, as with EF Core;
///     on the documents engine the limit is counted before the write, and two concurrent adds can cross it (owner's decision of 2026-10-26).
/// </summary>
internal sealed partial class TagActionsTemplate
{
    private const string Violation = "global::Pragmatic.Persistence.Repository.PersistenceRuleViolationException";
    private const string Spec = "global::Pragmatic.Specification.Spec";

    private void RenderEngineBody()
    {
        AppendLine($"private global::Pragmatic.Persistence.Repository.IRepository<{_model.TagTypeName}> _tags = null!;");
        AppendLine($"private global::Pragmatic.Persistence.Repository.IRepository<{_model.JunctionTypeName}> _links = null!;");
        AppendLine("private global::Pragmatic.Persistence.Repository.IUnitOfWork _unitOfWork = null!;");
        if (_kind == TagActionKind.Add)
        {
            AppendLine("private ICurrentUser _currentUser = null!;");
            AppendLine("private IClock _clock = null!;");
        }

        // The policy is optional, so it is resolved on demand from the application service
        // provider — injecting it directly would make it a required constructor dependency.
        AppendLine("private global::System.IServiceProvider _services = null!;");
        AppendLine();

        var returnType = _kind == TagActionKind.Add ? "Result<Guid, IError>" : "VoidResult<IError>";
        AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
        AppendLine(_kind == TagActionKind.Add ? "public required string TagValue { get; init; }" : "public required Guid TagId { get; init; }");
        AppendLine();

        AppendLine($"public override async Task<{returnType}> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            AppendLine("for (var attempt = 1; ; attempt++)");
            Block(() =>
            {
                AppendLine("try");
                Block(() => AppendLine("return await _unitOfWork.ExecuteAsync(ExecuteOnceAsync, ct).ConfigureAwait(false);"));
                AppendLine($"catch ({Violation} e) when (attempt < 3 && e.Error is global::Pragmatic.Persistence.Repository.ConcurrencyError or global::Pragmatic.Result.Database.DbConflictError)");
                Block(() =>
                {
                    AppendLine("// Another caller moved the tag's count, or minted the same tag or link, between the read and the save: read again.");
                    AppendLine("_unitOfWork.DiscardChanges();");
                });
            });
        });
        AppendLine();
        AppendLine($"private async Task<{returnType}> ExecuteOnceAsync(CancellationToken ct)");
        Block(_kind == TagActionKind.Add ? RenderEngineAddOnce : RenderEngineRemoveOnce);
    }

    private void RenderEngineAddOnce()
    {
        RenderPolicyGateAndNormalize();
        if (_serializable)
        {
            AppendLine("using var _tx = ((global::Pragmatic.Persistence.Sql.SqlUnitOfWork)_unitOfWork).Session.BeginTransaction(global::System.Data.IsolationLevel.Serializable);");
            AppendLine();
        }

        var fk = _model.ParentFkPropertyName;
        AppendLine($"var tag = (await _tags.FindAsync({Spec}<{_model.TagTypeName}>.Where(t => t.Value == normalizedValue && t.Scope == {ScopeLiteral}), ct).ConfigureAwait(false)).FirstOrDefault();");
        // Idempotency before the limit, as with EF Core: re-applying a tag adds nothing, so an entity at its limit still answers the tag's id.
        AppendLine($"if (tag is not null && await _links.ExistsAsync({Spec}<{_model.JunctionTypeName}>.Where(j => j.{fk} == {fk} && j.TagId == tag.Id), ct).ConfigureAwait(false))");
        IncreaseIndent();
        AppendLine("return tag.Id; // idempotent — already tagged");
        DecreaseIndent();
        AppendLine();

        if (_model.MaxPerEntity > 0)
        {
            AppendLine($"if (await _links.CountAsync({Spec}<{_model.JunctionTypeName}>.Where(j => j.{fk} == {fk}), ct).ConfigureAwait(false) >= {_model.MaxPerEntity})");
            IncreaseIndent();
            AppendLine($"return Result<Guid, IError>.Failure(new ForbiddenError {{ Resource = \"Tag limit ({_model.MaxPerEntity})\" }});");
            DecreaseIndent();
            AppendLine();
        }

        AppendLine("if (tag is null)");
        Block(() =>
        {
            // AllowCustom == false → curated taxonomy: never mint tags on the fly.
            if (!_model.AllowCustom)
            {
                AppendLine($"return Result<Guid, IError>.Failure(NotFoundError.For(\"{_model.TagTypeName}\", normalizedValue));");
                return;
            }

            AppendLine($"tag = new {_model.TagTypeName} {{ Value = normalizedValue, DisplayValue = displayValue, Scope = {ScopeLiteral}, CreatedAt = _clock.UtcNow, CreatedBy = _currentUser.Id, UsageCount = 1 }};");
            AppendLine("_tags.Add(tag);");
        });
        AppendLine("else");
        IncreaseIndent();
        AppendLine("tag.UsageCount++; // on the tracked row, its version checked by the save");
        DecreaseIndent();
        AppendLine();
        AppendLine($"_links.Add(new {_model.JunctionTypeName} {{ {fk} = {fk}, TagId = tag.Id, Value = tag.Value, DisplayValue = tag.DisplayValue, Scope = tag.Scope, AddedAt = _clock.UtcNow, AddedBy = _currentUser.Id }});");
        AppendLine("await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);");
        if (_serializable)
            AppendLine("_tx.Commit();");
        AppendLine($"if (_policy is not null) await _policy.OnTagAddedAsync({fk}, tag.Id, normalizedValue, ct).ConfigureAwait(false);");
        AppendLine("return tag.Id;");
    }

    private void RenderEngineRemoveOnce()
    {
        var fk = _model.ParentFkPropertyName;
        RenderPolicyResolve();
        AppendLine($"var link = (await _links.FindAsync({Spec}<{_model.JunctionTypeName}>.Where(j => j.{fk} == {fk} && j.TagId == TagId), ct).ConfigureAwait(false)).FirstOrDefault();");
        AppendLine("if (link is null)");
        IncreaseIndent();
        AppendLine($"return VoidResult<IError>.Failure(NotFoundError.For(\"{_model.JunctionTypeName}\", $\"{{TagId}}\"));");
        DecreaseIndent();
        AppendLine();
        AppendLine("_links.Remove(link);");
        AppendLine("var tag = await _tags.GetByIdAsync(TagId, ct).ConfigureAwait(false);");
        AppendLine("if (tag is not null && tag.UsageCount > 0)");
        IncreaseIndent();
        AppendLine("tag.UsageCount--; // on the tracked row, its version checked by the save");
        DecreaseIndent();
        if (_model.AllowCustom)
        {
            // A curated taxonomy (AllowCustom = false) is seeded by the application and recreated by no endpoint: its tags stay.
            AppendLine("var linkId = link.PersistenceId;");
            AppendLine($"if (tag is not null && tag.UsageCount <= 0 && !await _links.ExistsAsync({Spec}<{_model.JunctionTypeName}>.Where(j => j.TagId == TagId && j.PersistenceId != linkId), ct).ConfigureAwait(false))");
            IncreaseIndent();
            AppendLine("_tags.Remove(tag); // on its read version: a caller who linked it meanwhile keeps it");
            DecreaseIndent();
        }

        AppendLine("await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);");
        AppendLine($"if (_policy is not null) await _policy.OnTagRemovedAsync({fk}, TagId, tag?.Value ?? string.Empty, ct).ConfigureAwait(false);");
        AppendLine("return Success;");
    }
}
