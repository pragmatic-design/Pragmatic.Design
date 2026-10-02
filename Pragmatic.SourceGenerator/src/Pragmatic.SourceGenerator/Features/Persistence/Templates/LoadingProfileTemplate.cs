using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates a loading profile class that applies Include/ThenInclude calls
///     and optional AsSplitQuery to an IQueryable.
/// </summary>
internal sealed class LoadingProfileTemplate : CSharpTemplate
{
    private readonly LoadingProfileModel _model;

    public LoadingProfileTemplate(LoadingProfileModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} LoadingProfile from {_model.Namespace}";
    protected override string? TriggerInfo => $"[LoadWith] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "LoadingProfile", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model is { IsValid: true, HasNavigations: true };

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("System.Linq");

        AppendNamespace(_model.Namespace);
        AppendLine();

        var className = NamingHelper.AppendSuffix(_model.TypeName, "LoadingProfile");

        XmlSummary(
            $"Generated loading profile for <see cref=\"{_model.TypeName}\"/>. " +
            $"Applies Include calls up to depth {_model.MaxDepth}.");

        Class(className, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        RenderIncludePaths();
        AppendLine();

        XmlSummary($"Applies navigation includes to the query for {_model.EntityTypeName}.");
        XmlParam("query", "The source queryable.");
        XmlReturns("The queryable with includes applied.");

        Method("ApplyIncludes", RenderApplyBody,
            $"global::System.Linq.IQueryable<{_model.EntityFullTypeName}>",
            new List<MethodParameter>
            {
                new($"global::System.Linq.IQueryable<{_model.EntityFullTypeName}>", "query")
                {
                    IsExtension = true
                }
            },
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     The same paths, as the strings the read path consumes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="RenderApplyBody" /> renders a typed <c>Include(e =&gt; e.Nav)</c> chain, which
    ///         is what a hand-written caller wants and what <c>IIncludableQuery</c> cannot use: the
    ///         executor's loop calls <c>Include(string)</c>. So the list is published both ways from one
    ///         model, and the generated query names this one — rather than a second mechanism being
    ///         invented beside the loop that already runs.
    ///     </para>
    /// </remarks>
    private void RenderIncludePaths()
    {
        XmlSummary("The navigation paths this profile loads, in the form the query executor applies.");

        var paths = string.Join(", ", _model.NavigationPaths.Select(p => $"\"{p}\""));

        AppendLine(
            "public static global::System.Collections.Generic.IReadOnlyList<string> IncludePaths => "
            + $"[{paths}];");
    }

    private void RenderApplyBody()
    {
        // Apply includes
        AppendLine("query = query");
        IncreaseIndent();

        foreach (var path in _model.NavigationPaths)
        {
            // Split dot-separated paths for ThenInclude
            var parts = path.Split('.');
            if (parts.Length == 1)
            {
                AppendLine($".Include(e => e.{parts[0]})");
            }
            else
            {
                AppendLine($".Include(e => e.{parts[0]})");
                for (var i = 1; i < parts.Length; i++)
                    AppendLine($".ThenInclude(e => e.{parts[i]})");
            }
        }

        DecreaseIndent();

        if (_model.SplitQuery)
        {
            AppendLine("    .AsSplitQuery();");
        }
        else
        {
            AppendLine("    ;");
        }

        AppendLine();
        AppendLine("return query;");
    }
}
