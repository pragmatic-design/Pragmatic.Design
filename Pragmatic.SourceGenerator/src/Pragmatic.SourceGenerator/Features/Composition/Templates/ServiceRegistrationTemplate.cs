// Pragmatic.Composition - ServiceRegistrationTemplate
// Template for generating service registration extension methods

using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Template for generating ServiceRegistrationExtensions class.
/// </summary>
internal sealed class ServiceRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<DecoratorModel> _decorators;
    private readonly string _namespacePrefix;
    private readonly ImmutableArray<ServiceModel> _services;

    public ServiceRegistrationTemplate(
        ImmutableArray<ServiceModel> services,
        ImmutableArray<DecoratorModel> decorators,
        string? namespacePrefix = null)
    {
        _services = services;
        _decorators = decorators;
        _namespacePrefix = namespacePrefix ?? DeriveNamespacePrefix(services, decorators);
    }

    protected override string? GeneratorName => "Pragmatic.Composition";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForAssembly("DI", "ServiceRegistration"),
            ToSourceText());
    }

    /// <summary>
    ///     Derives a unique namespace prefix from the services and decorators.
    ///     Uses the most common root namespace segment.
    /// </summary>
    private static string DeriveNamespacePrefix(
        ImmutableArray<ServiceModel> services,
        ImmutableArray<DecoratorModel> decorators)
    {
        var allNamespaces = services
            .Select(s => s.Namespace)
            .Concat(decorators.Select(d => d.Namespace))
            .Where(ns => !string.IsNullOrEmpty(ns))
            .ToList();

        if (allNamespaces.Count == 0)
            return string.Empty;

        return NamespacePrefixHelper.DerivePrefix(allNamespaces);
    }

    protected override bool Validate()
    {
        return !_services.IsDefaultOrEmpty || !_decorators.IsDefaultOrEmpty;
    }

    public override void RenderFile()
    {
        var needsActivatorUtilities = _services.Any(s => s.RequiresFactory);
        var hasDecorators = !_decorators.IsDefaultOrEmpty;

        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Microsoft.Extensions.DependencyInjection.Extensions");
        if (hasDecorators)
            AddUsing("Pragmatic.Composition.Extensions");

        AppendNamespace(_namespacePrefix);
        AppendLine();

        RenderExtensionClass();
    }

    // =========================================================================
    // Extension Class
    // =========================================================================

    private void RenderExtensionClass()
    {
        XmlSummary("Extension methods for registering services discovered by the source generator.");

        Class("ServiceRegistrationExtensions", RenderMethods,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, IsStatic = true });
    }

    private void RenderMethods()
    {
        RenderAddPragmaticServicesMethod();
    }

    // =========================================================================
    // AddPragmaticServices Method
    // =========================================================================

    private void RenderAddPragmaticServicesMethod()
    {
        XmlSummary("Registers all services marked with [Service] attribute.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("IServiceCollection", "services") { IsExtension = true }
        };

        Method("AddPragmaticServices", RenderMethodBody, "IServiceCollection", parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderMethodBody()
    {
        // Register services
        foreach (var service in _services.OrderBy(s => s.FullTypeName))
            RenderServiceRegistration(service);

        if (!_services.IsDefaultOrEmpty && !_decorators.IsDefaultOrEmpty)
        {
            AppendLine();
            Comment("Apply decorators");
        }

        // Apply decorators in order
        var orderedDecorators = _decorators
            .OrderBy(d => d.DecoratedInterface)
            .ThenBy(d => d.Order);

        foreach (var decorator in orderedDecorators)
            AppendLine($"services.Decorate<{decorator.DecoratedInterface}, {decorator.FullTypeName}>();");

        AppendLine();
        Return("services");
    }

    // =========================================================================
    // Service Registration
    // =========================================================================

    private void RenderServiceRegistration(ServiceModel service)
    {
        if (service.IsOpenGeneric)
        {
            // Open generic registration
            var registrationMethod = GetRegistrationMethod(service.Lifetime);
            AppendLine(
                $"services.{registrationMethod}({service.OpenGenericServiceTypeName}, {service.OpenGenericImplementationTypeName});");
        }
        else if (service.RequiresFactory)
        {
            // Factory registration for services with [Inject] members
            RenderFactoryRegistration(service);
        }
        else if (service.Key is not null)
        {
            // Keyed service registration
            AppendLine(
                $"services.AddKeyed{service.Lifetime}<{service.ServiceTypeName}, {service.FullTypeName}>(\"{service.Key}\");");
        }
        else if (service.IsMultiple)
        {
            // One among several: TryAddEnumerable keeps every distinct implementation, and only
            // refuses the same implementation twice.
            AppendLine("services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor"
                       + $".{service.Lifetime}<{service.ServiceTypeName}, {service.FullTypeName}>());");
        }
        else
        {
            // Standard registration
            var registrationMethod = GetRegistrationMethod(service.Lifetime);
            AppendLine($"services.{registrationMethod}<{service.ServiceTypeName}, {service.FullTypeName}>();");
        }
    }

    private void RenderFactoryRegistration(ServiceModel service)
    {
        var factoryParts = new List<string>();

        // Create instance via ActivatorUtilities
        factoryParts.Add($"var instance = ActivatorUtilities.CreateInstance<{service.FullTypeName}>(sp);");

        // Set injected properties
        foreach (var prop in service.PropertyInjections)
            factoryParts.Add(GeneratePropertyInjection(prop));

        // Call injection methods
        foreach (var method in service.MethodInjections)
            factoryParts.Add(GenerateMethodInjection(method));

        factoryParts.Add("return instance;");

        var factoryBody = string.Join(" ", factoryParts);

        if (service.Key is not null)
        {
            // Keyed factory: a factory-registered service (has [Inject] members) that also declares a
            // key must stay resolvable by key — a non-keyed factory would drop it.
            AppendLine($"services.AddKeyed{service.Lifetime}<{service.ServiceTypeName}>(\"{service.Key}\", (sp, _) => {{ {factoryBody} }});");
        }
        else
        {
            var registrationMethod = GetRegistrationMethod(service.Lifetime);
            AppendLine($"services.{registrationMethod}<{service.ServiceTypeName}>(sp => {{ {factoryBody} }});");
        }
    }

    // =========================================================================
    // Injection Helpers
    // =========================================================================

    private static string GeneratePropertyInjection(PropertyInjectionModel prop)
    {
        if (prop.Key is not null)
        {
            var keyedMethod = prop.IsRequired ? "GetRequiredKeyedService" : "GetKeyedService";
            return $"instance.{prop.PropertyName} = sp.{keyedMethod}<{prop.PropertyTypeName}>(\"{prop.Key}\");";
        }

        if (prop.IsRequired)
            return $"instance.{prop.PropertyName} = sp.GetRequiredService<{prop.PropertyTypeName}>();";

        return $"instance.{prop.PropertyName} = sp.GetService<{prop.PropertyTypeName}>();";
    }

    private static string GenerateMethodInjection(MethodInjectionModel method)
    {
        var args = string.Join(", ", method.Parameters.Select(p =>
        {
            if (p.Key is not null)
            {
                var keyedMethod = p.IsOptional ? "GetKeyedService" : "GetRequiredKeyedService";
                return $"sp.{keyedMethod}<{p.FullTypeName}>(\"{p.Key}\")";
            }

            return p.IsOptional
                ? $"sp.GetService<{p.FullTypeName}>()"
                : $"sp.GetRequiredService<{p.FullTypeName}>()";
        }));

        return $"instance.{method.MethodName}({args});";
    }

    private static string GetRegistrationMethod(string lifetime)
    {
        // TryAdd prevents silent override when multiple generators register the same service
        return lifetime switch
        {
            "Singleton" => "TryAddSingleton",
            "Scoped" => "TryAddScoped",
            "Transient" => "TryAddTransient",
            _ => "TryAddScoped"
        };
    }
}
