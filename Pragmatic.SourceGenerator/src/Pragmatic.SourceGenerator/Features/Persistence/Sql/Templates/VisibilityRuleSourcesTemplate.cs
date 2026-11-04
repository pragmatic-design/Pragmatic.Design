using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     <c>[assembly: VisibilityRuleSource&lt;TRule&gt;("…")]</c> for each Pragmatic visibility rule this assembly declares: the source another
///     module's engine translates the rule from, when it reads this assembly's entities (<c>Transforms.RuleSources</c>).
/// </summary>
internal sealed class VisibilityRuleSourcesTemplate(List<(string Rule, string Predicate)> rules) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Persistence.Sql";

    protected override string? SourceInfo => "the source of the visibility rules this assembly declares, for an engine reading them from another assembly";

    public override Artifact RenderOutput() => new(VirtualFolderHints.ForMetadata("VisibilityRules"), ToSourceText());

    protected override bool Validate() => rules.Count > 0;

    public override void RenderFile()
    {
        foreach (var (rule, predicate) in rules)
            AppendLine($"[assembly: global::Pragmatic.Persistence.Entity.VisibilityRuleSource<{rule}>({SymbolDisplay.FormatLiteral(predicate, quote: true)})]");
    }
}
