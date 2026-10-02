using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates per-DTO include extension methods on IQueryable&lt;TEntity&gt;.
///     Each DTO gets a WithIncludesFor{Dto}() method that includes only the navigations it needs.
///     Also generates WithAllRelations() as a fallback that includes all same-boundary navigations.
/// </summary>
internal sealed class EntityIncludeTemplate : CSharpTemplate
{
    private readonly DtoIncludeModel _model;

    public EntityIncludeTemplate(DtoIncludeModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.EntityTypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"Include extensions for {_model.EntityTypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.EntityTypeName, "Includes", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("System.Linq");
        AddUsing("Microsoft.EntityFrameworkCore");

        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary(
            $"Include extension methods for <see cref=\"{_model.EntityTypeName}\"/> generated from mapped DTOs.");

        Class($"{_model.EntityTypeName}IncludeExtensions", RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        var entityParam = $"global::{_model.EntityFullTypeName}";

        // Per-DTO include methods
        foreach (var dto in _model.Dtos)
        {
            XmlSummary($"Includes relations needed by {dto.DtoTypeName}.");
            var parameters = new List<MethodParameter>
            {
                new($"this IQueryable<{entityParam}>", "query")
            };

            Method(
                $"WithIncludesFor{dto.DtoTypeName}",
                () => RenderIncludeLoop(dto),
                $"IQueryable<{entityParam}>",
                parameters,
                modifiers: new MethodModifiers { IsStatic = true });

            AppendLine();
        }

        // WithAllRelations fallback
        XmlSummary("Includes all same-boundary relations (fallback for manual use).");
        var allParams = new List<MethodParameter>
        {
            new($"this IQueryable<{entityParam}>", "query")
        };

        ExpressionMethod(
            "WithAllRelations",
            BuildIncludeChain("query", _model.AllNavigationNames),
            $"IQueryable<{entityParam}>",
            allParams,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     Loads what the DTO reaches through, from the list the DTO itself publishes.
    /// </summary>
    /// <remarks>
    ///     The string overload of <c>Include</c> rather than a chain of lambdas: the paths are known to
    ///     the DTO, not to this template, and a dotted one — <c>"Lines.Product"</c> — is a nesting a
    ///     lambda chain cannot express in a loop anyway.
    /// </remarks>
    private void RenderIncludeLoop(DtoIncludeEntry dto)
    {
        AppendLine($"foreach (var path in global::{dto.DtoFullTypeName}.RequiredNavigations)");
        IncreaseIndent();
        AppendLine("query = query.Include(path);");
        DecreaseIndent();
        AppendLine("return query;");
    }

    private static string BuildIncludeChain(string queryVar, IReadOnlyList<string> navigations)
    {
        if (navigations.Count == 0)
            return queryVar;

        var chain = queryVar;
        foreach (var nav in navigations)
        {
            chain += $".Include(e => e.{nav})";
        }

        return chain;
    }
}
