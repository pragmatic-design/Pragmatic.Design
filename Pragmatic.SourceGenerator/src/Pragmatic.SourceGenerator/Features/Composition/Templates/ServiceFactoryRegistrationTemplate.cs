using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates <c>PragmaticServiceFactories.AddPragmaticServiceFactories()</c> for the host: each
///     <c>[ServiceFactory]</c> class is registered as a singleton and every <c>[Factory]</c> method
///     registers its return type via a factory that calls the method (parameters resolved from DI).
///     The class is always emitted in host mode so the generated entry point can call it unconditionally.
/// </summary>
internal sealed class ServiceFactoryRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<ServiceFactoryModel> _factories;
    private readonly string _namespacePrefix;

    public ServiceFactoryRegistrationTemplate(ImmutableArray<ServiceFactoryModel> factories, string namespacePrefix)
    {
        _factories = factories;
        _namespacePrefix = namespacePrefix;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Composition", "ServiceFactoryRegistration"),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");

        AppendNamespace(_namespacePrefix);
        AppendLine();

        XmlSummary("Extension methods for registering [ServiceFactory] classes discovered by the source generator.");
        Class("PragmaticServiceFactories", RenderMethods,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, IsStatic = true });
    }

    private void RenderMethods()
    {
        XmlSummary("Registers all [ServiceFactory] classes and their [Factory] methods.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("IServiceCollection", "services") { IsExtension = true }
        };

        Method("AddPragmaticServiceFactories", RenderMethodBody, "IServiceCollection", parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderMethodBody()
    {
        foreach (var factory in _factories.OrderBy(f => f.FactoryClassFullName))
        {
            // The factory class itself is a singleton (per the [ServiceFactory] contract).
            AppendLine($"services.AddSingleton<{factory.FactoryClassFullName}>();");

            foreach (var method in factory.Methods)
            {
                var args = string.Join(", ", method.Parameters.Select(p => p.IsOptional
                    ? $"sp.GetService<{p.FullTypeName}>()"
                    : $"sp.GetRequiredService<{p.FullTypeName}>()"));

                AppendLine(
                    $"services.Add{method.Lifetime}<{method.ReturnTypeFullName}>(sp => sp.GetRequiredService<{factory.FactoryClassFullName}>().{method.MethodName}({args}));");
            }
        }

        AppendLine();
        Return("services");
    }
}
