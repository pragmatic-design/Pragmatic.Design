using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Registers one typed <see cref="N:Pragmatic.Persistence.RollUp"/> rule per <c>[RollUp]</c> (#2),
///     from a uniquely named public entry point the Composition host calls.
/// </summary>
/// <remarks>
///     <para>
///         It lives in the module assembly because the rule's <c>ApplyToParent</c> reaches the parent's
///         <c>internal</c> apply method, which nothing outside that assembly can.
///     </para>
///     <para>
///         ⚠️ It is not <c>RegisterRollUpRules</c>, the partial hook declared by the repository
///         registration and invoked only from <c>AddPragmaticPersistenceRepositories&lt;TDbContext&gt;()</c>
///         — the per-assembly entry point an application calls itself, and which the Pragmatic host
///         path does not call. Registered from the hook alone, the interceptor would ask the container
///         for rules and find none, and every <c>[RollUp]</c> in a generated application would be
///         inert, with no error anywhere. The hook is implemented too, so an application that wires
///         persistence by hand works, and it delegates here.
///     </para>
/// </remarks>
internal sealed class RollUpRegistrationTemplate : CSharpTemplate
{
    private const string RuleType = "global::Pragmatic.Persistence.RollUp.RollUpRule";
    private const string ServiceCollection =
        "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";

    private readonly IReadOnlyList<RollUpModel> _rollups;
    private readonly string _rootNamespace;

    public RollUpRegistrationTemplate(IReadOnlyList<RollUpModel> rollups, string rootNamespace)
    {
        _rollups = rollups;
        _rootNamespace = rootNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    public override Artifact RenderOutput() => new("_Infra.RollUp.Registration.g.cs", ToSourceText());

    protected override bool Validate() => _rollups.Count > 0;

    public override void RenderFile() => RenderPublicEntryPoint();

    /// <summary>The entry point the host calls, named per assembly so two modules never collide.</summary>
    private void RenderPublicEntryPoint()
    {
        AppendLine($"namespace {GeneratedRegistrationNames.InGeneratedNamespace(_rootNamespace)};");
        AppendLine();

        XmlSummary("Registers this assembly's [RollUp] rules. Called by the generated host.");
        AppendLine($"public static class {GeneratedRegistrationNames.RollUpRulesClass}");
        AppendLine("{");
        IncreaseIndent();

        // A sentinel, because both entry points are legitimate: a Pragmatic host calls this one, and an
        // application wiring persistence by hand reaches it through the partial hook. Registering the
        // rules twice would apply every delta twice, which is worse than not applying it at all — a
        // wrong number reads as data.
        XmlSummary("Marks this assembly's rules as registered, so the two entry points cannot double them.");
        AppendLine("private sealed class AlreadyRegistered;");
        AppendLine();

        var parameters = new List<MethodParameter>
        {
            new(ServiceCollection, "services") { IsExtension = true }
        };

        Method(GeneratedRegistrationNames.RollUpRulesMethod, () =>
        {
            AppendLine("foreach (var descriptor in services)");
            IncreaseIndent();
            AppendLine("if (descriptor.ServiceType == typeof(AlreadyRegistered)) return services;");
            DecreaseIndent();
            AppendLine();
            AppendLine("global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions"
                       + ".AddSingleton<AlreadyRegistered>(services);");
            AppendLine();

            foreach (var r in _rollups)
                RenderRule(r);

            AppendLine("return services;");
        }, ServiceCollection, parameters, AccessModifier.Public, new MethodModifiers { IsStatic = true });

        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderRule(RollUpModel r)
    {
        AppendLine($"global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<{RuleType}>(services,");
        AppendLine($"    new {RuleType}<{r.ChildFullName}, {r.ParentFullName}>");
        AppendLine("    {");
        IncreaseIndent();
        AppendLine($"AggregatePropertyName = \"{r.RollupProperty}\",");
        AppendLine($"ParentKey = c => c.{r.ChildForeignKeyProperty},");
        // A count reads nothing from the child: each one is worth one, and the interceptor's sign
        // does the rest — plus on insert, minus on delete.
        AppendLine(r.IsCount
            ? "Amount = c => 1m,"
            : $"Amount = c => c.{r.ChildAmountProperty},");
        AppendLine($"ApplyToParent = (p, d) => p.__ApplyRollUp_{r.RollupProperty}(d)");
        DecreaseIndent();
        AppendLine("    });");
    }
}
