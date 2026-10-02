using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>_Infra.Messaging.BusResolver.g.cs</c> — compile-time handler → bus mapping
///     from <c>[OnBus("name")]</c> attributes.
/// </summary>
/// <remarks>
///     Emits a real <see cref="P:Pragmatic.Messaging.IBusResolver"/> implementation
///     (instance method + singleton) so the resolver is DI-consumable and replaces
///     <c>DefaultBusResolver</c>. The static <c>GetBusName</c> overload is retained for callers
///     that resolve the bus name without DI.
/// </remarks>
internal sealed class BusResolverTemplate : CSharpTemplate
{
    private readonly ImmutableArray<MessageHandlerModel> _handlers;

    public BusResolverTemplate(ImmutableArray<MessageHandlerModel> handlers)
        => _handlers = handlers;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Messaging", "BusResolver"),
        ToSourceText());

    protected override bool Validate()
        => _handlers.Any(h => h.BusName is not null);

    public override void RenderFile()
    {
        AppendNamespace("Pragmatic.Messaging.Generated");
        AppendLine();

        XmlSummary("SG-generated bus resolver: maps handler types to named buses from [OnBus] attributes.");
        Class("PragmaticBusResolver", RenderBody,
            interfaces: ["global::Pragmatic.Messaging.IBusResolver"],
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        XmlSummary("Shared singleton instance for DI registration.");
        AppendLine("public static readonly PragmaticBusResolver Instance = new();");
        AppendLine();

        // Instance member satisfies IBusResolver; delegates to the static map so both surfaces agree.
        XmlSummary("Returns the target bus name for the given handler type, or null for default bus.");
        Method("GetBusName", () => AppendLine("return ResolveBusName(handlerTypeFqn);"),
            "string?",
            new List<MethodParameter> { new("string", "handlerTypeFqn") },
            AccessModifier.Public);

        AppendLine();

        // Static overload: usable without DI, and the source of truth for the instance method.
        XmlSummary("Returns the target bus name for the given handler type, or null for default bus.");
        Method("ResolveBusName", () =>
        {
            AppendLine("return handlerTypeFqn switch");
            AppendLine("{");
            IncreaseIndent();

            foreach (var handler in _handlers.Where(h => h.BusName is not null).OrderBy(h => h.TypeName))
            {
                var fqn = string.IsNullOrEmpty(handler.Namespace)
                    ? handler.TypeName
                    : $"{handler.Namespace}.{handler.TypeName}";

                AppendLine($"\"{fqn}\" => \"{handler.BusName}\",");
            }

            AppendLine("_ => null,");
            DecreaseIndent();
            AppendLine("};");
        },
        "string?",
        new List<MethodParameter> { new("string", "handlerTypeFqn") },
        AccessModifier.Public,
        new MethodModifiers { IsStatic = true });
    }
}
