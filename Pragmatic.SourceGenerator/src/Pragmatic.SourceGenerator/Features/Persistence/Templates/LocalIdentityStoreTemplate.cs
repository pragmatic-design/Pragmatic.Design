using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

using static Pragmatic.SourceGenerator.Core.TemplateHelpers;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates <c>{User}.LocalIdentityStore</c>: the <c>ILocalIdentityStore</c> of a <c>[PragmaticUser]</c>
///     entity that owns a <c>LocalIdentity</c>.
/// </summary>
/// <remarks>
///     <para>
///         Every application that signs its users in locally wrote this class by hand, the same way: find
///         the user by a column of its owned identity, hand back the identity, save. Two examples wrote it
///         twice, with one difference that was a defect — only one of them normalized the email — and one
///         thing both got wrong, which is saving through the <c>DbContext</c> instead of the unit of work.
///     </para>
///     <para>
///         ⚠️ The finders load the <b>user</b>, tracked, and return its identity. The identity actions
///         change what they found — a failed attempt, a reset token, a new password — and then call
///         <c>UpdateAsync</c>, which can only save what the context tracks. A projection of the owned
///         identity would be a detached copy: every change answered with success and lost.
///     </para>
///     <para>
///         Saved through the boundary's unit of work, like every other write: it fills generated values,
///         applies the retry strategy and classifies what the database refuses. A lost update still
///         surfaces as EF's own exception, as it did from the hand-written stores — the contract returns
///         nothing to carry it in.
///     </para>
/// </remarks>
internal sealed class LocalIdentityStoreTemplate : CSharpTemplate
{
    /// <summary>The nested type name.</summary>
    public const string ClassName = "LocalIdentityStore";

    private readonly EntityMetadataModel _model;

    public LocalIdentityStoreTemplate(EntityMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[PragmaticUser] with a LocalIdentity on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, ClassName, _model.Namespace),
        ToSourceText());

    protected override bool Validate()
        => _model.IsValid && !_model.IsFromReference && !string.IsNullOrEmpty(_model.LocalIdentityProperty);

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        Class(_model.TypeName, RenderStore,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderStore()
    {
        var entity = $"global::{_model.FullTypeName}";
        var keyed = RepositoryTemplate.KeyedServiceAttribute(_model);

        XmlSummary($"The local credentials of <see cref=\"{_model.TypeName}\"/>, read and written for the identity actions.");
        AppendLine($"public sealed class {ClassName}(");
        IncreaseIndent();
        AppendLine($"global::Pragmatic.Persistence.Repository.IRepository<{entity}> users,");
        AppendLine($"{keyed}global::Pragmatic.Persistence.Repository.IUnitOfWork unitOfWork)");
        AppendLine(": global::Pragmatic.Identity.Local.Services.ILocalIdentityStore");
        DecreaseIndent();
        Block(RenderMembers);
    }

    private void RenderMembers()
    {
        var identity = _model.LocalIdentityProperty;

        XmlInheritDoc();
        AppendLine("public async global::System.Threading.Tasks.ValueTask<global::Pragmatic.Identity.Local.LocalIdentity?> FindByEmailAsync(string email, global::System.Threading.CancellationToken ct = default)");
        IncreaseIndent();
        AppendLine($"=> (await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(users.Query(), u => u.{identity}!.Email == email, ct).ConfigureAwait(false))?.{identity};");
        DecreaseIndent();
        AppendLine();

        XmlInheritDoc();
        AppendLine("public async global::System.Threading.Tasks.ValueTask<global::Pragmatic.Identity.Local.LocalIdentity?> FindByExternalKeyAsync(string externalKey, global::System.Threading.CancellationToken ct = default)");
        IncreaseIndent();
        AppendLine($"=> (await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(users.Query(), u => u.{identity}!.ExternalIdentityKey == externalKey, ct).ConfigureAwait(false))?.{identity};");
        DecreaseIndent();
        AppendLine();

        RenderCreate();
        AppendLine();

        XmlInheritDoc();
        AppendLine("public async global::System.Threading.Tasks.ValueTask UpdateAsync(global::Pragmatic.Identity.Local.LocalIdentity identity, global::System.Threading.CancellationToken ct = default)");
        IncreaseIndent();
        Comment("The identity is owned by a tracked user: saving the unit of work writes what changed.");
        AppendLine("=> await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);");
        DecreaseIndent();
        AppendLine();

        XmlInheritDoc();
        AppendLine("public async global::System.Threading.Tasks.ValueTask<bool> EmailExistsAsync(string email, global::System.Threading.CancellationToken ct = default)");
        IncreaseIndent();
        AppendLine($"=> await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(users.Query(), u => u.{identity}!.Email == email, ct).ConfigureAwait(false);");
        DecreaseIndent();
    }

    private void RenderCreate()
    {
        var entity = $"global::{_model.FullTypeName}";

        XmlInheritDoc();
        if (_model.RegistersFromLocalIdentity)
        {
            AppendLine("public async global::System.Threading.Tasks.ValueTask<global::Pragmatic.Identity.Local.LocalIdentity> CreateAsync(global::Pragmatic.Identity.Local.LocalIdentity identity, global::System.Threading.CancellationToken ct = default)");
            Block(() =>
            {
                AppendLine($"users.Add({entity}.Register(identity));");
                AppendLine("await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);");
                AppendLine("return identity;");
            });
            return;
        }

        Comment($"{_model.TypeName} does not implement ISelfRegisteringUser: the application creates its users itself.");
        AppendLine("public global::System.Threading.Tasks.ValueTask<global::Pragmatic.Identity.Local.LocalIdentity> CreateAsync(global::Pragmatic.Identity.Local.LocalIdentity identity, global::System.Threading.CancellationToken ct = default)");
        IncreaseIndent();
        AppendLine("=> throw new global::System.NotSupportedException(");
        AppendLine($"    \"{_model.TypeName} is not created by self-registration: the application provisions it. \" +");
        AppendLine($"    \"Implement ISelfRegisteringUser<{_model.TypeName}> to let RegisterUser create one.\");");
        DecreaseIndent();
    }
}
