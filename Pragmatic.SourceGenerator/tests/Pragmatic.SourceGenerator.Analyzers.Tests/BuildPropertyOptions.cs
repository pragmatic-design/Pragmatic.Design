using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.SourceGenerator.Analyzers.Tests;

/// <summary>MSBuild properties as an analyzer sees them: <c>build_property.{Name}</c> global options.</summary>
internal sealed class BuildPropertyOptions(IReadOnlyDictionary<string, string> properties) : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(properties);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

    private sealed class Options(IReadOnlyDictionary<string, string> properties) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            const string prefix = "build_property.";
            if (key.StartsWith(prefix, System.StringComparison.Ordinal)
                && properties.TryGetValue(key.Substring(prefix.Length), out var found))
            {
                value = found;
                return true;
            }

            value = "";
            return false;
        }
    }
}
