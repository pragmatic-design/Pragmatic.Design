using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Emits the parent entity's internal apply methods for its roll-ups (#2). Generated into the parent's own
///     partial class so it can write the (private-set) aggregate property; the rule registration calls it.
/// </summary>
internal sealed class RollUpEntityTemplate : CSharpTemplate
{
    private readonly string _parentShortName;
    private readonly string _namespace;
    private readonly IReadOnlyList<RollUpModel> _rollups;

    public RollUpEntityTemplate(string parentShortName, string parentNamespace, IReadOnlyList<RollUpModel> rollups)
    {
        _parentShortName = parentShortName;
        _namespace = parentNamespace;
        _rollups = rollups;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    /// <remarks>
    ///     The hint carries the namespace, like every other per-type output. Built by hand as
    ///     <c>{Parent}.RollUp.g.cs</c>, two entities sharing a simple name across namespaces would
    ///     produce the same hint. Roslyn answers a duplicate hint by discarding <b>this generator's
    ///     entire output</b> under a CS8785 <em>warning</em>: without <c>--warnaserror</c> the build
    ///     goes green with every generated file missing.
    /// </remarks>
    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_parentShortName, "RollUp", _namespace), ToSourceText());

    protected override bool Validate() => _rollups.Count > 0;

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_namespace))
        {
            AppendNamespace(_namespace);
            AppendLine();
        }

        AppendLine($"partial class {_parentShortName}");
        AppendLine("{");
        IncreaseIndent();
        foreach (var r in _rollups)
        {
            // The pipeline carries decimal for every rule; the aggregate is whatever the author
            // declared. This is the one place that knows both, so the conversion belongs here — a
            // count in an int is `+= (int)delta`, and without it the generated partial does not build.
            var delta = r.RollupPropertyTypeName is "decimal" or "global::System.Decimal"
                ? "delta"
                : $"({r.RollupPropertyTypeName})delta";

            AppendLine(
                $"internal void __ApplyRollUp_{r.RollupProperty}(decimal delta) "
                + $"=> {r.RollupProperty} += {delta};");
        }
        DecreaseIndent();
        AppendLine("}");
    }
}
