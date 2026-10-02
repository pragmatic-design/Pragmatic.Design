using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

internal sealed class NoteActionsTemplate : CSharpTemplate
{
    private readonly NoteTraitModel _model;
    private readonly string _actionTypeName;
    private readonly NoteActionKind _kind;

    public NoteActionsTemplate(NoteTraitModel model, NoteActionKind kind)
    {
        _model = model;
        _kind = kind;
        _actionTypeName = kind switch
        {
            NoteActionKind.Add => $"Add{model.ParentTypeName}NoteAction",
            NoteActionKind.Update => $"Update{model.ParentTypeName}NoteAction",
            NoteActionKind.Delete => $"Delete{model.ParentTypeName}NoteAction",
            NoteActionKind.GetById => $"Get{model.ParentTypeName}NoteAction",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasNotes] {_kind} action for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_actionTypeName, "Action", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System.Linq");
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Pragmatic.Actions.Abstractions");
        AddUsing("Pragmatic.Actions.Attributes");
        // [BelongsTo<T>] lives in Persistence, not beside [DomainAction].
        AddUsing("Pragmatic.Persistence.Entity");
        AddUsing("Pragmatic.Identity");
        AddUsing("Pragmatic.Notes");
        AddUsing("Pragmatic.Result");
        AddUsing("Pragmatic.Result.Http");
        AddUsing("Pragmatic.Temporal.Clock");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        var dtoName = $"{_model.NoteTypeName}Dto";
        var baseType = _kind switch
        {
            NoteActionKind.Add => "DomainAction<Guid>",
            NoteActionKind.GetById => $"DomainAction<{dtoName}>",
            _ => "VoidDomainAction"
        };

        XmlSummary($"SG-generated {_kind.ToString().ToLowerInvariant()} note action for <see cref=\"{_model.ParentTypeName}\"/>.");
        AppendLine("[DomainAction]");
        if (_model.BoundaryFullTypeName is not null)
            AppendLine($"[BelongsTo<global::{_model.BoundaryFullTypeName}>]");

        Class(_actionTypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, Sealed = true },
            baseType: baseType);
    }

    private void RenderBody()
    {
        AppendLine("private DbContext _db = null!;");
        AppendLine("private ICurrentUser _currentUser = null!;");
        AppendLine("private IClock _clock = null!;");
        // Carries the application container in, so the optional IPermissionChecker can be resolved
        // without becoming a required constructor dependency.
        AppendLine("private global::System.IServiceProvider _services = null!;");
        AppendLine();

        switch (_kind)
        {
            case NoteActionKind.Add: RenderAddBody(); break;
            case NoteActionKind.GetById: RenderGetByIdBody(); break;
            case NoteActionKind.Update: RenderUpdateBody(); break;
            case NoteActionKind.Delete: RenderDeleteBody(); break;
        }

        if (_kind is NoteActionKind.Update or NoteActionKind.Delete)
            RenderCanModerate();
    }

    private void RenderAddBody()
    {
        AppendLine($"public required {_model.SimpleIdType} {_model.ParentTypeName}Id {{ get; init; }}");
        AppendLine("public required string Content { get; init; }");
        AppendLine();
        AppendLine("public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            // MaxLength was only a column width: an over-long body failed at SaveChanges as a 500.
            AppendLine($"if (Content.Length > {_model.MaxLength})");
            IncreaseIndent();
            AppendLine($"return Result<Guid, IError>.Failure(new BadRequestError {{ Reason = \"Content exceeds the maximum length of {_model.MaxLength} characters.\" }});");
            DecreaseIndent();
            AppendLine();

            AppendLine($"var note = new {_model.NoteTypeName}");
            AppendLine("{");
            IncreaseIndent();
            AppendLine($"{_model.ParentFkPropertyName} = {_model.ParentTypeName}Id,");
            AppendLine("Content = Content,");
            AppendLine("AuthorId = _currentUser.Id,");
            AppendLine("AuthorName = _currentUser.DisplayName ?? string.Empty,");
            AppendLine("CreatedAt = _clock.UtcNow,");
            DecreaseIndent();
            AppendLine("};");
            AppendLine($"_db.Set<{_model.NoteTypeName}>().Add(note);");
            // Commit is owned by the DomainActionInvoker (single SaveChangesAsync after Execute). Do NOT save here.
            AppendLine("return note.Id;");
        });
    }

    private void RenderGetByIdBody()
    {
        var dtoName = $"{_model.NoteTypeName}Dto";
        // Parent FK is part of object-level auth: the note must belong to the parent in the route.
        AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
        AppendLine("public required Guid NoteId { get; init; }");
        AppendLine();
        AppendLine($"public override async Task<Result<{dtoName}, IError>> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            AppendLine($"var note = await _db.Set<{_model.NoteTypeName}>()");
            AppendLine($"    .Where(e => e.Id == NoteId && e.{_model.ParentFkPropertyName} == {_model.ParentFkPropertyName} && !e.IsDeleted)");
            AppendLine($"    .Select({dtoName}.Projection)");
            AppendLine("    .FirstOrDefaultAsync(ct).ConfigureAwait(false);");
            AppendLine("if (note is null)");
            IncreaseIndent();
            AppendLine($"return Result<{dtoName}, IError>.Failure(NotFoundError.For(\"{_model.NoteTypeName}\", NoteId.ToString()));");
            DecreaseIndent();
            AppendLine("return note;");
        });
    }

    private void RenderUpdateBody()
    {
        AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
        AppendLine("public required Guid NoteId { get; init; }");
        AppendLine("public required string Content { get; init; }");
        AppendLine();
        AppendLine("public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            AppendLine($"if (Content.Length > {_model.MaxLength})");
            IncreaseIndent();
            AppendLine($"return VoidResult<IError>.Failure(new BadRequestError {{ Reason = \"Content exceeds the maximum length of {_model.MaxLength} characters.\" }});");
            DecreaseIndent();
            AppendLine();

            // Object-level auth: scope the lookup to the parent in the route, not just the child id.
            AppendLine($"var note = await _db.Set<{_model.NoteTypeName}>()");
            AppendLine($"    .Where(e => e.Id == NoteId && e.{_model.ParentFkPropertyName} == {_model.ParentFkPropertyName})");
            AppendLine("    .FirstOrDefaultAsync(ct).ConfigureAwait(false);");
            AppendLine("if (note is null)");
            IncreaseIndent();
            AppendLine($"return VoidResult<IError>.Failure(NotFoundError.For(\"{_model.NoteTypeName}\", NoteId.ToString()));");
            DecreaseIndent();
            AppendLine();
            // The author, or someone who can moderate. Without the bypass the update permission was
            // insufficient by definition: whoever held it and was not the author always got a 403.
            AppendLine("if (note.AuthorId != _currentUser.Id && !await CanModerateAsync(ct))");
            IncreaseIndent();
            AppendLine("return VoidResult<IError>.Failure(new ForbiddenError { Resource = \"Note\" });");
            DecreaseIndent();
            AppendLine();

            if (_model.EditWindowMinutes >= 0)
            {
                AppendLine($"if (_clock.UtcNow > note.CreatedAt.AddMinutes({_model.EditWindowMinutes}))");
                IncreaseIndent();
                AppendLine("return VoidResult<IError>.Failure(new ForbiddenError { Resource = \"Note edit window\" });");
                DecreaseIndent();
                AppendLine();
            }

            AppendLine("note.Content = Content;");
            AppendLine("note.IsEdited = true;");
            AppendLine("note.UpdatedAt = _clock.UtcNow;");
            AppendLine("note.UpdatedBy = _currentUser.Id;");
            // Commit is owned by the DomainActionInvoker (single SaveChangesAsync after Execute). Do NOT save here.
            AppendLine("return Success;");
        });
    }

    private void RenderDeleteBody()
    {
        AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
        AppendLine("public required Guid NoteId { get; init; }");
        AppendLine();
        AppendLine("public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            // Object-level auth: scope the lookup to the parent in the route, not just the child id.
            AppendLine($"var note = await _db.Set<{_model.NoteTypeName}>()");
            AppendLine($"    .Where(e => e.Id == NoteId && e.{_model.ParentFkPropertyName} == {_model.ParentFkPropertyName})");
            AppendLine("    .FirstOrDefaultAsync(ct).ConfigureAwait(false);");
            AppendLine("if (note is null)");
            IncreaseIndent();
            AppendLine($"return VoidResult<IError>.Failure(NotFoundError.For(\"{_model.NoteTypeName}\", NoteId.ToString()));");
            DecreaseIndent();
            AppendLine();
            // Same rule as the update. Gated by the delete permission alone, deletion would let anyone
            // holding it remove another author's note while being unable to edit it.
            AppendLine("if (note.AuthorId != _currentUser.Id && !await CanModerateAsync(ct))");
            IncreaseIndent();
            AppendLine("return VoidResult<IError>.Failure(new ForbiddenError { Resource = \"Note\" });");
            DecreaseIndent();
            AppendLine();
            AppendLine("note.IsDeleted = true;");
            AppendLine("note.DeletedAt = _clock.UtcNow;");
            AppendLine("note.DeletedBy = _currentUser.Id;");
            // Commit is owned by the DomainActionInvoker (single SaveChangesAsync after Execute). Do NOT save here.
            AppendLine("return Success;");
        });
    }

    /// <summary>
    ///     Emits the moderator check used by update and delete. <c>IPermissionChecker</c> is optional:
    ///     without it nobody is a moderator and the author-only rule stands.
    /// </summary>
    private void RenderCanModerate()
    {
        AppendLine();
        XmlSummary("True when the caller holds the moderate permission for these notes.");
        AppendLine("private async Task<bool> CanModerateAsync(CancellationToken ct)");
        Block(() =>
        {
            AppendLine("var checker = (global::Pragmatic.Authorization.IPermissionChecker?)_services.GetService(typeof(global::Pragmatic.Authorization.IPermissionChecker));");
            AppendLine("if (checker is null) return false;");
            AppendLine($"return await checker.HasPermissionAsync(\"{TraitPermissions.Slug(_model.BoundaryName, _model.ParentTypeName, TraitPermissions.NotesGroup, TraitPermissions.Moderate)}\", ct).ConfigureAwait(false);");
        });
    }
}

internal enum NoteActionKind
{
    Add,
    GetById,
    Update,
    Delete,
}
