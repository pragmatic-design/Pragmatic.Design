using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Roll-up feature (#2): collects <c>[RollUp&lt;TChild&gt;]</c> parent properties and emits each
///     parent's internal apply method, the typed <c>RollUpRule</c> registrations, and the two channels
///     that get the host to call them. The runtime <c>RollUpInterceptor</c> consumes the rules.
/// </summary>
/// <remarks>
///     ⚠️ The registration is not reachable only through <c>RegisterRollUpRules</c>, the partial hook
///     invoked by <c>AddPragmaticPersistenceRepositories&lt;TDbContext&gt;()</c> — the per-assembly
///     entry point an application calls itself, and which the Pragmatic host path does not call.
///     Through the hook alone, the interceptor would be wired and ask the container for rules, the
///     container would have none, and every <c>[RollUp]</c> in a generated application would be inert
///     with no error anywhere. So it is published the way the <c>Redaction</c> metadata category is:
///     a metadata attribute for a referenced module, and a local entry for the host's own compilation.
/// </remarks>
internal static class RollUpFeature
{
    private const string RollUpAttributeFullName = "Pragmatic.Persistence.Entity.RollUpAttribute`1";

    public static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context)
    {
        var rollups = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                RollUpAttributeFullName,
                static (node, _) => node is PropertyDeclarationSyntax,
                RollUpTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect();

        var assemblyName = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "");
        var all = rollups.Combine(assemblyName);

        context.RegisterSourceOutputSafe(all, static (ctx, data) =>
        {
            var (models, assembly) = data;

            var valid = models.Where(m => m.IsValid).ToList();
            if (valid.Count == 0)
                return;

            // Parent partial apply methods, one file per parent.
            foreach (var byParent in valid.GroupBy(m => m.ParentFullName))
            {
                var list = byParent.ToList();
                var first = list[0];
                var artifact = new RollUpEntityTemplate(first.ParentShortName, first.ParentNamespace, list).RenderOutput();
                if (!artifact.IsEmpty)
                    ctx.AddSource(artifact);
            }

            var registration = new RollUpRegistrationTemplate(valid, assembly).RenderOutput();
            if (!registration.IsEmpty)
                ctx.AddSource(registration);

            // The partial hook, in its own file: a source file carries one namespace declaration, and
            // the hook belongs to Pragmatic.Persistence.Generated while the entry point above belongs
            // to this assembly's own generated namespace.
            var hook = new RollUpHookTemplate(assembly).RenderOutput();
            if (!hook.IsEmpty)
                ctx.AddSource(hook);

            // The assembly attribute a referenced module publishes. The MetadataEntry below covers only
            // the host's own compilation; without this one a module's rules are emitted, registered by
            // a method nobody calls, and the aggregate is never maintained.
            var metadata = new RollUpMetadataTemplate(assembly).RenderOutput();
            if (!metadata.IsEmpty)
                ctx.AddSource(metadata);
        });

        // Under the same condition that emits the registration, tell the host to call it.
        return all.Select(static (data, _) =>
        {
            var (models, assembly) = data;

            if (!models.Any(m => m.IsValid))
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            return new EquatableArray<Composition.Models.MetadataEntry>(
                ImmutableArray.Create(
                    Composition.Models.HostLocalRegistration.Create(
                        Composition.MetadataCategoryIds.RollUpRules,
                        "1.0",
                        Core.GeneratedRegistrationNames.RollUpRulesFqn(assembly))));
        });
    }
}
