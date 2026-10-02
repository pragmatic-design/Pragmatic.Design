using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates per-DTO query extension methods on IQueryable&lt;TEntity&gt;.
///     Each DTO with [GenerateProjection] gets:
///     - As{Dto}() → IQueryable&lt;TDto&gt; (compose includes + projection)
///     - GetAs{Dto}Async(id, ct) → TDto? (single by id)
///     - ListAs{Dto}Async(ct) → List&lt;TDto&gt; (all projected)
/// </summary>
internal sealed class DtoQueryExtensionsTemplate : CSharpTemplate
{
    private readonly DtoQueryModel _model;

    public DtoQueryExtensionsTemplate(DtoQueryModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.EntityTypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"DTO query extensions for {_model.EntityTypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.EntityTypeName, "Projections", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Collections.Generic");
        AddUsing("System.Linq");
        AddUsing("System.Threading");
        AddUsing("System.Threading.Tasks");
        AddUsing("Microsoft.EntityFrameworkCore");

        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary(
            $"DTO query extension methods for <see cref=\"{_model.EntityTypeName}\"/>.");

        Class($"{_model.EntityTypeName}DtoQueryExtensions", RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        var entityParam = $"global::{_model.EntityFullTypeName}";
        var first = true;

        foreach (var dto in _model.Dtos)
        {
            if (!first)
                AppendLine();
            first = false;

            var dtoParam = $"global::{dto.DtoFullTypeName}";

            RenderAsMethod(entityParam, dtoParam, dto);
            AppendLine();
            RenderGetAsMethod(entityParam, dtoParam, dto);
            AppendLine();
            RenderListAsMethod(entityParam, dtoParam, dto);
        }
    }

    /// <summary>
    ///     Projects the query to the DTO. No include: a projection is already a JOIN.
    /// </summary>
    /// <remarks>
    ///     Not <c>query.WithIncludesFor{Dto}().Select({Dto}.Projection)</c>. Measured against SQLite
    ///     (<c>IncludeBeforeProjectionTests</c>), EF drops an include whose entity does not survive into
    ///     the result — silently, without refusing the query — so the call would be useless rather than
    ///     harmful, and nothing would report it. The include belongs where the entity itself comes back.
    /// </remarks>
    private void RenderAsMethod(string entityParam, string dtoParam, DtoQueryEntry dto)
    {
        XmlSummary($"Projects the query to {dto.DtoTypeName}.");
        var parameters = new List<MethodParameter>
        {
            new($"this IQueryable<{entityParam}>", "query")
        };

        ExpressionMethod(
            $"As{dto.DtoTypeName}",
            $"query.Select({dtoParam}.Projection)",
            $"IQueryable<{dtoParam}>",
            parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderGetAsMethod(string entityParam, string dtoParam, DtoQueryEntry dto)
    {
        XmlSummary($"Gets a single {_model.EntityTypeName} projected to {dto.DtoTypeName}.");
        XmlParam("query", "The source query.");
        XmlParam("id", "The entity identifier.");
        XmlParam("ct", "Cancellation token.");

        var idType = _model.IdType;
        var parameters = new List<MethodParameter>
        {
            new($"this IQueryable<{entityParam}>", "query"),
            new(idType, "id"),
            new("CancellationToken", "ct") { DefaultValue = "default" }
        };

        Method($"GetAs{dto.DtoTypeName}Async", () =>
        {
            Return("await query\n" +
                   $"                .Where(e => e.PersistenceId == id)\n" +
                   $"                .Select({dtoParam}.Projection)\n" +
                   $"                .FirstOrDefaultAsync(ct)");
        }, $"async Task<{dtoParam}?>", parameters,
            accessModifier: AccessModifier.Public,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderListAsMethod(string entityParam, string dtoParam, DtoQueryEntry dto)
    {
        XmlSummary($"Lists {_model.EntityTypeName} entities projected to {dto.DtoTypeName}.");
        XmlParam("query", "The source query.");
        XmlParam("ct", "Cancellation token.");

        var parameters = new List<MethodParameter>
        {
            new($"this IQueryable<{entityParam}>", "query"),
            new("CancellationToken", "ct") { DefaultValue = "default" }
        };

        Method($"ListAs{dto.DtoTypeName}Async", () =>
        {
            Return($"await query.As{dto.DtoTypeName}().ToListAsync(ct)");
        }, $"async Task<List<{dtoParam}>>", parameters,
            accessModifier: AccessModifier.Public,
            modifiers: new MethodModifiers { IsStatic = true });
    }
}
