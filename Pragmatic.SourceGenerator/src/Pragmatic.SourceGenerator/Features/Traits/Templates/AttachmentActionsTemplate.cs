using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

internal sealed class AttachmentActionsTemplate : CSharpTemplate
{
    private readonly AttachmentTraitModel _model;
    private readonly string _actionTypeName;
    private readonly AttachmentActionKind _kind;
    private readonly bool _imagingAvailable;

    /// <param name="model">The trait as the attribute declared it.</param>
    /// <param name="kind">Which of the four actions to render.</param>
    /// <param name="imagingAvailable">
    ///     Whether the compilation references Pragmatic.Imaging. ⚠️ A constructor argument rather than
    ///     a model field: the model is read from the attribute, and this is read from the compilation
    ///     by <c>FeatureDetector</c>. Keeping them apart is what makes it impossible to emit a call
    ///     into a package the consumer does not have — the emission is decided here, at compile time,
    ///     instead of by a nullable service asked for at run time.
    /// </param>
    public AttachmentActionsTemplate(
        AttachmentTraitModel model, AttachmentActionKind kind, bool imagingAvailable = false)
    {
        _model = model;
        _kind = kind;
        _imagingAvailable = imagingAvailable;
        _actionTypeName = kind switch
        {
            AttachmentActionKind.Upload => $"Upload{model.ParentTypeName}AttachmentAction",
            AttachmentActionKind.Delete => $"Delete{model.ParentTypeName}AttachmentAction",
            AttachmentActionKind.GetById => $"Get{model.ParentTypeName}AttachmentAction",
            AttachmentActionKind.Download => $"Download{model.ParentTypeName}AttachmentAction",
            AttachmentActionKind.DownloadThumbnail => $"DownloadThumbnail{model.ParentTypeName}AttachmentAction",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasAttachments] {_kind} action for {_model.ParentTypeName}";

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
        AddUsing("Pragmatic.Attachments");
        // Only the download action names FileResponse; the others must not drag a
        // Pragmatic.Endpoints using into modules that reference only Actions.
        if (_kind is AttachmentActionKind.Download or AttachmentActionKind.DownloadThumbnail)
            AddUsing("Pragmatic.Endpoints.Responses");
        AddUsing("Pragmatic.Identity");
        AddUsing("Pragmatic.Result");
        AddUsing("Pragmatic.Result.Http");
        AddUsing("Pragmatic.Storage");
        AddUsing("Pragmatic.Temporal.Clock");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        var dtoName = $"{_model.AttachmentTypeName}Dto";
        var baseType = _kind switch
        {
            AttachmentActionKind.Upload => "DomainAction<Guid>",
            AttachmentActionKind.GetById => $"DomainAction<{dtoName}>",
            AttachmentActionKind.Download or AttachmentActionKind.DownloadThumbnail => "DomainAction<FileResponse>",
            _ => "VoidDomainAction"
        };

        XmlSummary($"SG-generated {_kind.ToString().ToLowerInvariant()} attachment action for <see cref=\"{_model.ParentTypeName}\"/>.");
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
        if (_kind is AttachmentActionKind.Upload or AttachmentActionKind.Delete)
        {
            AppendLine("private ICurrentUser _currentUser = null!;");
            AppendLine("private IClock _clock = null!;");
        }
        if (_kind is AttachmentActionKind.Upload or AttachmentActionKind.Download
            or AttachmentActionKind.DownloadThumbnail)
            AppendLine("private IFileStorage _storage = null!;");
        AppendLine();

        switch (_kind)
        {
            case AttachmentActionKind.Upload: RenderUploadBody(); break;
            case AttachmentActionKind.GetById: RenderGetByIdBody(); break;
            case AttachmentActionKind.Download: RenderDownloadBody(); break;
            case AttachmentActionKind.DownloadThumbnail: RenderDownloadThumbnailBody(); break;
            case AttachmentActionKind.Delete: RenderDeleteBody(); break;
        }
    }

    /// <summary>
    ///     Whether this upload derives a thumbnail: the attribute asked for one <em>and</em> the
    ///     compilation can perform it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Both halves, and the second is not a fallback. Emitting the call without the reference
    ///     would break the consumer's build inside generated code, which says nothing about the
    ///     attribute that caused it; emitting a nullable service and asking at run time would produce
    ///     a branch that is never taken and looks like one that is. The case where the attribute asks
    ///     and the reference is missing is reported as PRAG2651 instead of guessed at.
    /// </remarks>
    private bool Thumbnails => _model.ThumbnailRequested && _imagingAvailable;

    /// <summary>
    ///     Reads the upload into memory once, because the bytes are needed twice.
    /// </summary>
    /// <remarks>
    ///     The alternative is to read the file back out of storage after writing it, which is a second
    ///     round trip to a bucket that was just written to — and, on a provider with read-after-write
    ///     that is not immediate, a read that can miss. The cost is the file's size in memory, which
    ///     <c>MaxFileSizeBytes</c> already bounds; the size is re-checked here because a caller-supplied
    ///     <c>FileSize</c> on a non-seekable stream is a claim, and now there is a fact to compare it to.
    /// </remarks>
    private void RenderBufferForThumbnail()
    {
        AppendLine("using var _buffered = new global::System.IO.MemoryStream();");
        AppendLine("await FileContent.CopyToAsync(_buffered, ct).ConfigureAwait(false);");
        AppendLine("var _bytes = _buffered.ToArray();");
        if (_model.MaxFileSizeBytes > 0)
        {
            AppendLine($"if (_bytes.LongLength > {_model.MaxFileSizeBytes}L)");
            IncreaseIndent();
            AppendLine($"return Result<Guid, IError>.Failure(new ForbiddenError {{ Resource = \"File size exceeds {FormatSizeLimit(_model.MaxFileSizeBytes)} limit\" }});");
            DecreaseIndent();
        }

        AppendLine("effectiveSize = _bytes.LongLength;");
        AppendLine("_buffered.Position = 0;");
        AppendLine();
    }

    /// <summary>Derives the thumbnail and stores it beside the original.</summary>
    private void RenderThumbnail()
    {
        AppendLine("// The thumbnail is derived here, where the file is stored: a resize on the read");
        AppendLine("// path would be a resize per viewer. It is a DERIVED file — regenerable from the");
        AppendLine("// original, never served in its place, and losing it is not data loss.");
        AppendLine("global::System.Uri? thumbnailUri = null;");
        AppendLine();
        AppendLine("// The extension is the caller's CLAIM that this is an image, and it is what keeps a");
        AppendLine("// PDF or a CSV away from the decoder entirely. Whether the claim was true is the");
        AppendLine("// bytes' answer, below, and a claim that turns out to be false is a refusal.");
        AppendLine("if (global::Pragmatic.Imaging.ImageFormats.IsKnownExtension(ext))");
        Block(() =>
        {
            AppendLine("try");
            Block(() =>
            {
                AppendLine("using var _image = global::Pragmatic.Imaging.ImagePipeline.Load(_bytes);");
                AppendLine($"var _thumbnail = _image.Thumbnail({_model.ThumbnailMaxWidth}, {_model.ThumbnailMaxHeight})");
                AppendLine("    .Encode(global::Pragmatic.Imaging.ImageFormat.WebP, quality: 80);");
                AppendLine();
                AppendLine("using var _thumbnailStream = new global::System.IO.MemoryStream(_thumbnail);");
                AppendLine("thumbnailUri = await _storage.SaveAsync(");
                AppendLine("    _thumbnailStream,");
                AppendLine("    System.IO.Path.GetFileNameWithoutExtension(FileName) + \"-thumbnail.webp\",");
                AppendLine($"    \"{_model.Container}\", ct).ConfigureAwait(false);");
            });
            AppendLine("catch (global::Pragmatic.Imaging.ImagingException)");
            Block(() =>
            {
                AppendLine("// A file that names an image format and does not decode will not decode later");
                AppendLine("// either. Refused now, while there is still a caller to tell — and the original");
                AppendLine("// is already written, so it goes back out.");
                AppendLine("await _storage.DeleteAsync(storageUri, ct).ConfigureAwait(false);");
                AppendLine("return Result<Guid, IError>.Failure(new BadRequestError");
                AppendLine("{");
                IncreaseIndent();
                AppendLine("Reason = \"The file names an image format and could not be decoded.\",");
                AppendLine("Field = nameof(FileName),");
                DecreaseIndent();
                AppendLine("});");
            });
        });
    }

    /// <summary>
    ///     Counts and inserts in one serializable transaction, inside the context's execution strategy.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Counting and inserting are two statements: under the default isolation two concurrent uploads
    ///         both read "one below the limit" and both insert. Serializable makes the pair indivisible.
    ///     </para>
    ///     <para>
    ///         Inside the strategy because one that retries — a generated host configures one for every server
    ///         provider — refuses a transaction opened outside it. Only this part runs again on a
    ///         transient failure: the upload above has consumed the caller's stream and is not repeatable, so
    ///         it stays outside, once, and a retry re-counts before inserting.
    ///     </para>
    /// </remarks>
    private void RenderLimitedInsert()
    {
        // The verification is what stops a retry after a lost acknowledgement: without it the strategy
        // runs the insert again, the same key collides, and the upload fails for a row that is there.
        AppendLine("var withinLimit = await _db.Database.CreateExecutionStrategy().ExecuteAsync(");
        AppendLine("    attachment,");
        AppendLine("    async (_, token) =>");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("await using var _tx = await _db.Database.BeginTransactionAsync(");
        AppendLine("    global::System.Data.IsolationLevel.Serializable, token).ConfigureAwait(false);");
        AppendLine($"var countInTransaction = await _db.Set<{_model.AttachmentTypeName}>()");
        AppendLine($"    .CountAsync(a => a.{_model.ParentFkPropertyName} == {_model.ParentFkPropertyName}, token).ConfigureAwait(false);");
        AppendLine($"if (countInTransaction >= {_model.MaxPerEntity})");
        AppendLine("    return false;");
        AppendLine();
        AppendLine("// Added again on a retry: a failed save leaves it Added, a failed commit leaves it Unchanged.");
        AppendLine($"_db.Set<{_model.AttachmentTypeName}>().Add(attachment);");
        AppendLine("await _db.SaveChangesAsync(token).ConfigureAwait(false);");
        AppendLine("await _tx.CommitAsync(token).ConfigureAwait(false);");
        AppendLine("return true;");
        DecreaseIndent();
        AppendLine("},");
        AppendLine("    async (_, token) => new global::Microsoft.EntityFrameworkCore.Storage.ExecutionResult<bool>(");
        AppendLine("        await AttachmentCommittedAsync(attachment.Id, token).ConfigureAwait(false), true),");
        AppendLine("    ct).ConfigureAwait(false);");
        AppendLine();
        AppendLine("if (!withinLimit)");
        Block(() =>
        {
            RenderDeleteThumbnailIfWritten();
            AppendLine("await _storage.DeleteAsync(storageUri, ct).ConfigureAwait(false);");
            AppendLine($"return Result<Guid, IError>.Failure(new ForbiddenError {{ Resource = \"Attachment limit ({_model.MaxPerEntity})\" }});");
        });
    }

    /// <summary>Takes the derived file back out when the row that would point at it never commits.</summary>
    private void RenderDeleteThumbnailIfWritten()
    {
        if (!Thumbnails) return;

        // The original has a row to be found by; the thumbnail has none at all, so a failure here
        // leaves bytes nothing in the system knows about — invisible even to the purge job, which
        // only ever looks at soft-deleted rows.
        AppendLine("if (thumbnailUri is not null)");
        IncreaseIndent();
        AppendLine("await _storage.DeleteAsync(thumbnailUri, ct).ConfigureAwait(false);");
        DecreaseIndent();
    }

    private void RenderUploadBody()
    {
        AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
        AppendLine("public required Stream FileContent { get; init; }");
        AppendLine("public required string FileName { get; init; }");
        AppendLine("public required long FileSize { get; init; }");
        AppendLine("public required string ContentType { get; init; }");
        AppendLine("public string? Description { get; init; }");
        AppendLine();

        AppendLine("public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            // A refusal before the upload, so a caller already at the limit does not send the file for
            // nothing. It is not the guarantee — two uploads can both pass it — the count inside the
            // serializable transaction below is.
            if (_model.MaxPerEntity > 0)
            {
                AppendLine($"var currentCount = await _db.Set<{_model.AttachmentTypeName}>()");
                AppendLine($"    .CountAsync(a => a.{_model.ParentFkPropertyName} == {_model.ParentFkPropertyName}, ct).ConfigureAwait(false);");
                AppendLine($"if (currentCount >= {_model.MaxPerEntity})");
                IncreaseIndent();
                AppendLine($"return Result<Guid, IError>.Failure(new ForbiddenError {{ Resource = \"Attachment limit ({_model.MaxPerEntity})\" }});");
                DecreaseIndent();
                AppendLine();
            }

            // Effective size: FileSize is supplied by the caller, so on its own it is a claim, not a
            // fact. Any in-process caller (another module, a job, the boundary interface) could pass a
            // small FileSize with a huge stream and walk past the limit. When the stream can report its
            // own length — which it can for an uploaded file — that length is the truth.
            AppendLine("var effectiveSize = FileContent.CanSeek ? FileContent.Length : FileSize;");
            AppendLine();

            // File size check
            if (_model.MaxFileSizeBytes > 0)
            {
                AppendLine($"if (effectiveSize > {_model.MaxFileSizeBytes}L)");
                IncreaseIndent();
                AppendLine($"return Result<Guid, IError>.Failure(new ForbiddenError {{ Resource = \"File size exceeds {FormatSizeLimit(_model.MaxFileSizeBytes)} limit\" }});");
                DecreaseIndent();
                AppendLine();
            }

            // Extension: always computed, because two checks depend on it.
            AppendLine("var ext = System.IO.Path.GetExtension(FileName)?.ToLowerInvariant() ?? \"\";");
            AppendLine();

            // An extension is reused verbatim as the suffix of the stored blob name, so anything that
            // is not a plain extension has no business reaching the storage provider — on Windows it
            // would throw out of File.Create and surface as a 500.
            AppendLine("if (ext.Length > 0 && (ext.Length > 24 || ext.Skip(1).Any(c => !char.IsLetterOrDigit(c))))");
            IncreaseIndent();
            AppendLine("return Result<Guid, IError>.Failure(new ForbiddenError { Resource = \"File extension is not a valid extension\" });");
            DecreaseIndent();
            AppendLine();

            if (!string.IsNullOrEmpty(_model.AllowedExtensions))
            {
                AppendLine($"var allowed = new[] {{ {FormatExtensionArray(_model.AllowedExtensions)} }};");
                AppendLine("if (!allowed.Contains(ext))");
                IncreaseIndent();
                AppendLine("return Result<Guid, IError>.Failure(new ForbiddenError { Resource = \"File extension not allowed\" });");
                DecreaseIndent();
                AppendLine();
            }
            else
            {
                // No allow-list configured: still refuse what a browser executes in the site's own
                // origin. LocalDiskFileStorage is documented to live under wwwroot and be served by
                // UseStaticFiles, so an uploaded .html or .svg would be reachable at its own URL —
                // without the attachments.read permission and outside any parent scoping. Listing such
                // an extension in AllowedExtensions is an explicit opt-in and bypasses this check.
                AppendLine($"var browserExecutable = new[] {{ {FormatExtensionArray(BrowserExecutableExtensions)} }};");
                AppendLine("if (browserExecutable.Contains(ext))");
                IncreaseIndent();
                AppendLine("return Result<Guid, IError>.Failure(new ForbiddenError { Resource = \"File extension is executable in a browser — list it in AllowedExtensions to permit it\" });");
                DecreaseIndent();
                AppendLine();
            }

            // The content type is client-supplied and is echoed back on download. Anything that is not
            // a well-formed `type/subtype` token pair is replaced rather than trusted.
            AppendLine("var safeContentType = IsWellFormedMediaType(ContentType) ? ContentType : \"application/octet-stream\";");
            AppendLine();

            // Save to storage
            if (Thumbnails)
            {
                RenderBufferForThumbnail();
                AppendLine($"var storageUri = await _storage.SaveAsync(_buffered, FileName, \"{_model.Container}\", ct).ConfigureAwait(false);");
            }
            else
            {
                AppendLine($"var storageUri = await _storage.SaveAsync(FileContent, FileName, \"{_model.Container}\", ct).ConfigureAwait(false);");
            }

            AppendLine();

            if (Thumbnails)
            {
                RenderThumbnail();
                AppendLine();
            }

            // Create metadata record
            AppendLine($"var attachment = new {_model.AttachmentTypeName}");
            AppendLine("{");
            IncreaseIndent();
            AppendLine($"{_model.ParentFkPropertyName} = {_model.ParentFkPropertyName},");
            AppendLine("FileName = FileName,");
            AppendLine("FileSize = effectiveSize,");
            AppendLine("ContentType = safeContentType,");
            AppendLine("StorageUri = storageUri.ToString(),");
            AppendLine("Description = Description,");
            AppendLine("UploadedBy = _currentUser.Id ?? \"\",");
            AppendLine("UploadedAt = _clock.UtcNow,");
            if (Thumbnails)
                AppendLine("ThumbnailUri = thumbnailUri?.ToString(),");
            DecreaseIndent();
            AppendLine("};");
            if (_model.MaxPerEntity <= 0)
                AppendLine($"_db.Set<{_model.AttachmentTypeName}>().Add(attachment);");
            AppendLine();

            // The blob is already written, but the row that points at it is not committed yet. If that
            // commit fails — a foreign key to a parent that does not exist, a concurrent limit breach —
            // the bytes would stay in storage with nothing referencing them: invisible to the purge job,
            // which only ever looks at soft-deleted ROWS. Commit here and undo the write on failure.
            AppendLine("try");
            Block(() =>
            {
                if (_model.MaxPerEntity > 0)
                    RenderLimitedInsert();
                else
                    AppendLine("await _db.SaveChangesAsync(ct).ConfigureAwait(false);");
            });
            if (_model.MaxPerEntity > 0)
            {
                AppendLine("catch (global::System.Exception ex) when (IsSerializationConflict(ex))");
                Block(() =>
                {
                    RenderDeleteThumbnailIfWritten();
                    AppendLine("await _storage.DeleteAsync(storageUri, ct).ConfigureAwait(false);");
                    AppendLine($"return Result<Guid, IError>.Failure(new ConflictError {{ EntityType = \"{_model.AttachmentTypeName}\", EntityId = {_model.ParentFkPropertyName}.ToString() }});");
                });
            }

            AppendLine("catch");
            Block(() =>
            {
                // A commit can throw after it has happened: the connection drops before the server's
                // answer arrives. Deleting the blob then leaves a committed row pointing at nothing, so
                // ask the database before undoing the write.
                AppendLine("if (await AttachmentCommittedAsync(attachment.Id, ct).ConfigureAwait(false))");
                IncreaseIndent();
                AppendLine("return attachment.Id;");
                DecreaseIndent();
                AppendLine();
                RenderDeleteThumbnailIfWritten();
                AppendLine("await _storage.DeleteAsync(storageUri, ct).ConfigureAwait(false);");
                AppendLine("throw;");
            });
            AppendLine();
            AppendLine("return attachment.Id;");
        });
        AppendLine();

        XmlSummary("Whether the row reached the database: an exception from its commit does not say it did not.");
        AppendLine("private async Task<bool> AttachmentCommittedAsync(Guid id, CancellationToken ct)");
        IncreaseIndent();
        AppendLine($"=> await _db.Set<{_model.AttachmentTypeName}>().AsNoTracking().IgnoreQueryFilters()");
        AppendLine("    .AnyAsync(a => a.Id == id, ct).ConfigureAwait(false);");
        DecreaseIndent();
        AppendLine();

        if (_model.MaxPerEntity > 0)
        {
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
            AppendLine();
        }

        // Emitted alongside the action so the check travels with the code that uses it.
        XmlSummary("True when the value is a well-formed <c>type/subtype</c> media type.");
        AppendLine("private static bool IsWellFormedMediaType(string? value)");
        Block(() =>
        {
            AppendLine("if (string.IsNullOrWhiteSpace(value)) return false;");
            AppendLine("var parts = value!.Split(';')[0].Trim().Split('/');");
            AppendLine("if (parts.Length != 2) return false;");
            AppendLine("static bool IsToken(string s) => s.Length > 0 && s.All(c =>");
            AppendLine("    char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '+' || c == '_');");
            AppendLine("return IsToken(parts[0]) && IsToken(parts[1]);");
        });
    }

    private void RenderGetByIdBody()
    {
        var dtoName = $"{_model.AttachmentTypeName}Dto";
        // Parent FK is part of object-level auth: the attachment must belong to the parent in the route.
        AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
        AppendLine("public required Guid AttachmentId { get; init; }");
        AppendLine();
        AppendLine($"public override async Task<Result<{dtoName}, IError>> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            AppendLine($"var attachment = await _db.Set<{_model.AttachmentTypeName}>()");
            AppendLine($"    .Where(e => e.Id == AttachmentId && e.{_model.ParentFkPropertyName} == {_model.ParentFkPropertyName} && !e.IsDeleted)");
            AppendLine($"    .Select({dtoName}.Projection)");
            AppendLine("    .FirstOrDefaultAsync(ct).ConfigureAwait(false);");
            AppendLine("if (attachment is null)");
            IncreaseIndent();
            AppendLine($"return Result<{dtoName}, IError>.Failure(NotFoundError.For(\"{_model.AttachmentTypeName}\", AttachmentId.ToString()));");
            DecreaseIndent();
            AppendLine("return attachment;");
        });
    }

    /// <summary>
    ///     Serves the derived preview, and says "no such thing" when there is none.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It never falls back to the original. A route asked for a thumbnail that answered with a
    ///     four-megabyte screenshot would be worse than answering nothing: the caller asked for the
    ///     small one precisely because it cannot afford the large one. A null column is a 404, which
    ///     is the same answer this action gives for an attachment that does not exist — and the DTO's
    ///     <c>HasThumbnail</c> is how a caller knows before asking.
    /// </remarks>
    private void RenderDownloadThumbnailBody()
    {
        AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
        AppendLine("public required Guid AttachmentId { get; init; }");
        AppendLine();
        AppendLine("public override async Task<Result<FileResponse, IError>> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            // Same scoping as the download it sits beside: the row must belong to the parent in the
            // route and not be soft-deleted, or anyone allowed to read one parent's attachments could
            // read every other parent's.
            AppendLine($"var file = await _db.Set<{_model.AttachmentTypeName}>()");
            AppendLine($"    .Where(e => e.Id == AttachmentId && e.{_model.ParentFkPropertyName} == {_model.ParentFkPropertyName} && !e.IsDeleted)");
            AppendLine("    .Select(e => new { e.ThumbnailUri, e.FileName, e.UploadedAt, e.Id })");
            AppendLine("    .FirstOrDefaultAsync(ct).ConfigureAwait(false);");
            AppendLine("if (file?.ThumbnailUri is null)");
            IncreaseIndent();
            AppendLine($"return Result<FileResponse, IError>.Failure(NotFoundError.For(\"{_model.AttachmentTypeName}\", AttachmentId.ToString()));");
            DecreaseIndent();
            AppendLine();
            AppendLine("var content = await _storage.GetAsync(new Uri(file.ThumbnailUri, UriKind.RelativeOrAbsolute), ct).ConfigureAwait(false);");
            AppendLine("if (content is null)");
            IncreaseIndent();
            AppendLine($"return Result<FileResponse, IError>.Failure(NotFoundError.For(\"{_model.AttachmentTypeName}\", AttachmentId.ToString()));");
            DecreaseIndent();
            AppendLine();
            // The content type is the generator's own, not the row's: the derivation encodes WebP
            // whatever the original was, so echoing the attachment's ContentType would label a WebP
            // as a PNG and browsers would decode it wrong or refuse it.
            AppendLine("return new FileResponse(content, \"image/webp\",");
            AppendLine("    System.IO.Path.GetFileNameWithoutExtension(file.FileName) + \"-thumbnail.webp\")");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("ETag = file.Id.ToString(\"N\") + \"-thumb\",");
            AppendLine("LastModified = file.UploadedAt,");
            DecreaseIndent();
            AppendLine("};");
        });
        AppendLine();
    }

    private void RenderDownloadBody()
    {
        // Same scoping as GetById: the row must belong to the parent in the route AND not be
        // soft-deleted. Matching on AttachmentId alone would let anyone allowed to read the
        // attachments of ONE parent download the attachments of every other parent (IDOR).
        AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
        AppendLine("public required Guid AttachmentId { get; init; }");
        AppendLine();
        AppendLine("public override async Task<Result<FileResponse, IError>> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            AppendLine($"var file = await _db.Set<{_model.AttachmentTypeName}>()");
            AppendLine($"    .Where(e => e.Id == AttachmentId && e.{_model.ParentFkPropertyName} == {_model.ParentFkPropertyName} && !e.IsDeleted)");
            AppendLine("    .Select(e => new { e.StorageUri, e.FileName, e.ContentType, e.UploadedAt, e.Id })");
            AppendLine("    .FirstOrDefaultAsync(ct).ConfigureAwait(false);");
            AppendLine("if (file is null)");
            IncreaseIndent();
            AppendLine($"return Result<FileResponse, IError>.Failure(NotFoundError.For(\"{_model.AttachmentTypeName}\", AttachmentId.ToString()));");
            DecreaseIndent();
            AppendLine();
            AppendLine("var content = await _storage.GetAsync(new Uri(file.StorageUri, UriKind.RelativeOrAbsolute), ct).ConfigureAwait(false);");
            AppendLine();
            // A blob removed out of band is an expected state, not a server fault: nothing is broken
            // here and no retry helps, so 500 would be wrong (and would page someone). 404 is the
            // honest answer — the request target has no representation. Not 410: that claims the
            // condition is permanent, which we cannot know (the blob may come back from a backup).
            AppendLine("// The row survives its blob (deleted out of band, lifecycle rule, restored DB");
            AppendLine("// snapshot). That is a missing representation, not a server error: 404, not 500.");
            AppendLine("if (content is null)");
            IncreaseIndent();
            AppendLine($"return Result<FileResponse, IError>.Failure(NotFoundError.For(\"{_model.AttachmentTypeName}\", AttachmentId.ToString()));");
            DecreaseIndent();
            AppendLine();
            AppendLine("return new FileResponse(content, file.ContentType, file.FileName)");
            AppendLine("{");
            IncreaseIndent();
            // An attachment's bytes never change once uploaded — the id is the version. Without these
            // every re-download transferred the whole file again: no 304, no conditional request.
            // Bare value: FileResponseExtensions wraps it in the quotes the header needs, so quoting
            // it here produced a malformed EntityTagHeaderValue and a 500 on every download.
            AppendLine("ETag = file.Id.ToString(\"N\"),");
            AppendLine("LastModified = file.UploadedAt,");
            DecreaseIndent();
            AppendLine("};");
        });
    }

    private void RenderDeleteBody()
    {
        AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
        AppendLine("public required Guid AttachmentId { get; init; }");
        AppendLine();
        AppendLine("public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            // Object-level auth: scope the lookup to the parent in the route, not just the child id.
            AppendLine($"var attachment = await _db.Set<{_model.AttachmentTypeName}>()");
            AppendLine($"    .Where(e => e.Id == AttachmentId && e.{_model.ParentFkPropertyName} == {_model.ParentFkPropertyName})");
            AppendLine("    .FirstOrDefaultAsync(ct).ConfigureAwait(false);");
            AppendLine("if (attachment is null)");
            IncreaseIndent();
            AppendLine($"return VoidResult<IError>.Failure(NotFoundError.For(\"{_model.AttachmentTypeName}\", AttachmentId.ToString()));");
            DecreaseIndent();
            AppendLine();
            AppendLine("attachment.IsDeleted = true;");
            AppendLine("attachment.DeletedAt = _clock.UtcNow;");
            AppendLine("attachment.DeletedBy = _currentUser.Id;");
            AppendLine();
            // B18: soft delete keeps the blob ON PURPOSE — the operation is reversible, so removing the
            // file here would destroy data that a restore is supposed to bring back. Reclaiming it is a
            // separate, scheduled decision: [HasAttachments(PurgeDeletedAfterDays = N)].
            AppendLine("// NOTE: the stored file is intentionally NOT removed from IFileStorage — this is a");
            AppendLine("// reversible soft delete, and dropping the bytes here would make a restore impossible.");
            if (_model.PurgeEnabled)
            {
                AppendLine($"// The blob is reclaimed later by {_model.PurgeJobTypeName}, which runs on the");
                AppendLine($"// [HasAttachments(PurgeDeletedAfterDays = {_model.PurgeDeletedAfterDays})] retention window.");
            }
            else
            {
                AppendLine("// No purge job is generated: set [HasAttachments(PurgeDeletedAfterDays = N)] to have one,");
                AppendLine("// otherwise reclaiming the storage stays the consuming application's responsibility.");
            }
            AppendLine("return Success;");
        });
    }

    /// <summary>
    ///     Normalizes the documented parsing contract of <c>AllowedExtensions</c>: trimmed,
    ///     lower-cased, leading dot optional. The comparison is against
    ///     <c>Path.GetExtension</c>, which always yields the dot — so "pdf" without it would
    ///     never match, silently rejecting every upload it was meant to allow.
    ///     This mirrors what TraitEndpointModelBuilder does for the endpoint-level check.
    /// </summary>
    private static string FormatExtensionArray(string extensions)
    {
        var parts = extensions
            .Split(',')
            .Select(p => p.Trim().ToLowerInvariant())
            .Where(p => p.Length > 0)
            .Select(p => p.StartsWith(".", StringComparison.Ordinal) ? p : "." + p);

        return string.Join(", ", parts.Select(p => $"\"{p}\""));
    }

    /// <summary>
    ///     Extensions a browser renders as active content in the origin that served them. Refused when
    ///     no explicit allow-list is configured; naming one in AllowedExtensions permits it.
    /// </summary>
    private const string BrowserExecutableExtensions =
        ".html,.htm,.xhtml,.shtml,.svg,.svgz,.xml,.js,.mjs,.mhtml,.hta";

    /// <summary>
    ///     Renders a byte limit the way a human reads it. Integer-dividing by a megabyte made every
    ///     limit below 1 MB read as "0MB", which tells the caller nothing about what to retry with.
    /// </summary>
    private static string FormatSizeLimit(long bytes)
    {
        const long Kilobyte = 1024L;
        const long Megabyte = Kilobyte * 1024L;

        if (bytes >= Megabyte && bytes % Megabyte == 0)
            return $"{bytes / Megabyte}MB";
        if (bytes >= Kilobyte && bytes % Kilobyte == 0)
            return $"{bytes / Kilobyte}KB";

        return $"{bytes} bytes";
    }
}

internal enum AttachmentActionKind
{
    Upload,
    GetById,
    Download,

    /// <summary>
    ///     The derived preview. A separate action from <see cref="Download" /> rather than a flag on
    ///     it: the two read different columns and answer differently when the column is null, and a
    ///     download that could quietly return a thumbnail instead of the file would be the exact
    ///     confusion between a record and a cache that the nullable column exists to prevent.
    /// </summary>
    DownloadThumbnail,
    Delete,
}
