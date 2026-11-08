using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     Generates <c>{User}EfUserLookup</c>: the <c>IUserLookup&lt;TUser&gt;</c> of a <c>[PragmaticUser]</c> EF Core persists, over the repository's
///     <c>Query()</c>.
/// </summary>
/// <remarks>
///     The resolver used to hold this query itself, which tied it to EF Core: on the engines <c>Query()</c> is refused. Here it is the one place
///     that names EF Core, and the resolver reads through the contract (#592).
/// </remarks>
internal sealed class EfUserLookupTemplate : CSharpTemplate
{
    private readonly UserEntityModel _model;

    public EfUserLookupTemplate(UserEntityModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Identity";

    /// <summary>The lookup type name, shared with whoever registers it.</summary>
    public static string LookupNameFor(UserEntityModel model) => NamingHelper.AppendSuffix(model.TypeName, "EfUserLookup");

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TypeName, "EfUserLookup", _model.Namespace), ToSourceText());

    protected override bool Validate() => !string.IsNullOrEmpty(_model.TypeName) && _model.LooksUpThroughEfCore;

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Pragmatic.Identity");
        AddUsing("Pragmatic.Persistence.Repository");

        if (!string.IsNullOrEmpty(_model.Namespace))
            AppendNamespace(_model.Namespace);

        AppendLine();

        XmlSummary($"Finds a <see cref=\"{_model.TypeName}\"/> by its <c>{_model.MatchProperty}</c>, through EF Core.");
        // The repository, not a DbContext: the boundary's context is generated in the host and cannot be named from the module, while
        // IReadRepository<TEntity> is registered by the framework and reaches across the assembly boundary.
        AppendLine($"{_model.Accessibility} sealed class {LookupNameFor(_model)}(IReadRepository<{_model.TypeName}> repository) : IUserLookup<{_model.TypeName}>");
        AppendLine("{");
        IncreaseIndent();
        XmlInheritDoc();
        AppendLine($"public async Task<{_model.TypeName}?> FindByIdentityKeyAsync(string identityKey, CancellationToken ct = default)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("if (string.IsNullOrEmpty(identityKey)) return null;");
        AppendLine();
        AppendLine("return await repository.Query()");
        AppendLine($"    .FirstOrDefaultAsync(u => u.{_model.MatchProperty} == identityKey, ct)");
        AppendLine("    .ConfigureAwait(false);");
        DecreaseIndent();
        AppendLine("}");
        DecreaseIndent();
        AppendLine("}");
    }
}
