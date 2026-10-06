using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Logging.Models;

namespace Pragmatic.SourceGenerator.Features.Logging.Templates;

/// <summary>
///     The file that implements a type's <c>[LoggerMessage]</c> methods: the type reopened, and each call
///     site written into it.
/// </summary>
internal sealed class LogCallSitesTemplate : LogCallSiteTemplateBase
{
    private readonly IReadOnlyList<LogCallSiteModel> _callSites;

    public LogCallSitesTemplate(IReadOnlyList<LogCallSiteModel> callSites) => _callSites = callSites;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Logging";

    protected override bool Validate() => _callSites.Count > 0;

    public override Artifact RenderOutput()
    {
        var first = _callSites[0];
        var typeName = string.Join(".", first.Containers.Select(c => HintSafe(c.Name)));
        return new Artifact(VirtualFolderHints.ForType(typeName, "LogCallSites", first.Namespace), ToSourceText());
    }

    public override void RenderFile()
    {
        var first = _callSites[0];
        if (first.Namespace.Length > 0)
            AppendNamespace(first.Namespace);
        AppendLine();

        RenderContainers(first.Containers.AsImmutableArray(), 0, () =>
        {
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            for (var i = 0; i < _callSites.Count; i++)
            {
                if (i > 0)
                    AppendLine();

                RenderCallSite(_callSites[i], StateName(_callSites[i], names));
            }
        });
    }

    private void RenderContainers(System.Collections.Immutable.ImmutableArray<LogContainerModel> containers, int index, System.Action body)
    {
        if (index == containers.Length)
        {
            body();
            return;
        }

        var container = containers[index];
        var modifier = (container.IsStatic ? "static " : "") + "partial " + container.Keyword + " " + container.Name;
        Block(() => RenderContainers(containers, index + 1, body), modifier);
    }

    private static string HintSafe(string name)
    {
        var open = name.IndexOf('<');
        return open < 0 ? name : name.Substring(0, open) + "_" + (name.Count(c => c == ',') + 1);
    }
}
