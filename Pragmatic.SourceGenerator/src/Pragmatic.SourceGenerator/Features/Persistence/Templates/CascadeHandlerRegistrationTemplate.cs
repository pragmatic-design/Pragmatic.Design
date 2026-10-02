using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates DI registration for every generated cascade handler in the assembly, registering each
///     as an <c>IDomainEventHandler&lt;EntityPropertyChanged&lt;TSource&gt;&gt;</c> so the domain-event
///     dispatcher actually invokes it.
/// </summary>
/// <remarks>
///     The cascade handler is emitted by this generator, but the event-handler discovery
///     (<c>[EventHandler]</c>) only scans user source — a generator cannot see another generator's
///     output in the same compilation. Without this registration the handler would never be
///     registered and the cascade would silently do nothing. This registration lives in the SAME assembly as the handlers (which are
///     <c>internal</c>), exposed as a public extension the host calls from its startup, exactly like
///     the lookup-cache / query-filter registrations.
/// </remarks>
internal sealed class CascadeHandlerRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<CascadeModel> _cascades;

    public CascadeHandlerRegistrationTemplate(ImmutableArray<CascadeModel> cascades) => _cascades = cascades;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/CascadeRegistration";
    protected override string? SourceInfo => $"Cascade handler registration for {_cascades.Length} cascade(s)";

    public override Artifact RenderOutput() =>
        new("_Infra.Persistence.CascadeHandlers.g.cs", ToSourceText());

    protected override bool Validate() => _cascades.Any(c => c.IsValid);

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");

        var namespaces = _cascades.Select(c => c.Namespace).Where(n => !string.IsNullOrEmpty(n));
        var prefix = NamespacePrefixHelper.DerivePrefix(namespaces);
        var identifierPrefix = NamespacePrefixHelper.ToIdentifier(prefix);

        if (!string.IsNullOrEmpty(prefix))
            AppendNamespace(prefix);
        AppendLine();

        var className = string.IsNullOrEmpty(identifierPrefix)
            ? "CascadeHandlerRegistrationExtensions"
            : $"{identifierPrefix}CascadeHandlerRegistrationExtensions";

        var methodName = string.IsNullOrEmpty(identifierPrefix)
            ? "AddCascadeHandlers"
            : $"Add{identifierPrefix}CascadeHandlers";

        XmlSummary("Registers all generated cascade handlers as domain-event handlers.");
        Class(className, () => RenderMethod(methodName),
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderMethod(string methodName)
    {
        XmlSummary("Registers each generated cascade handler as an IDomainEventHandler so cascades fire.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        Method(methodName, RenderBody,
            "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
            [new("this global::Microsoft.Extensions.DependencyInjection.IServiceCollection", "services")],
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        foreach (var cascade in _cascades.Where(c => c.IsValid))
        {
            var handlerFqn = $"global::{cascade.Namespace}.{cascade.TargetTypeName}{cascade.TargetProperty}CascadeHandler";
            var eventType = $"global::Pragmatic.Events.EntityPropertyChanged<{cascade.SourceQualifiedTypeName}>";
            var handlerInterface = $"global::Pragmatic.Events.IDomainEventHandler<{eventType}>";
            AppendLine($"services.AddScoped<{handlerInterface}, {handlerFqn}>();");
        }

        AppendLine();
        AppendLine("return services;");
    }
}
