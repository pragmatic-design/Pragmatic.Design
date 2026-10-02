// Pragmatic.Composition.SourceGenerator - Library Mode Generator

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition.Templates;

namespace Pragmatic.SourceGenerator.Features.Composition.Generators;

/// <summary>
///     Generates code for LIBRERIA mode (ClassLibrary projects).
///     Handles both startup composition and service registration.
/// </summary>
internal static class LibraryModeGenerator
{
    public static void Generate(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<StartupModel> startups,
        ImmutableArray<ServiceModel> services,
        ImmutableArray<DecoratorModel> decorators,
        ImmutableArray<ModuleModel> modules,
        ImmutableArray<EventHandlerModel> eventHandlers = default)
    {
        // Derive namespace prefix from modules, startups, services, decorators, and event handlers
        var namespacePrefix = DeriveNamespacePrefix(modules, startups, services, decorators, eventHandlers);
        var isDebug = IsDebugConfiguration(compilation);
        var hasServices = !services.IsDefaultOrEmpty || !decorators.IsDefaultOrEmpty;
        var hasStartups = startups.Length > 0;
        var hasEventHandlers = !eventHandlers.IsDefaultOrEmpty;

        // Generate service registration and metadata for services
        if (hasServices)
        {
            var serviceTemplate = new ServiceRegistrationTemplate(services, decorators, namespacePrefix);
            var serviceArtifact = serviceTemplate.RenderOutput();
            context.AddSource(serviceArtifact);

            // Generate enriched [PragmaticMetadata] for services (host generates DI code directly from this)
            var serviceMetadataTemplate = new ServiceMetadataTemplate(services, decorators, isDebug);
            var serviceMetadataArtifact = serviceMetadataTemplate.RenderOutput();
            context.AddSource(serviceMetadataArtifact);
        }

        // Generate pipeline step registration if we have pipeline steps
        if (hasStartups)
        {
            var pipelineStepTemplate = new PipelineStepRegistrationTemplate(namespacePrefix, startups);
            var pipelineStepArtifact = pipelineStepTemplate.RenderOutput();
            context.AddSource(pipelineStepArtifact);

            // Generate [PragmaticMetadata] attribute
            var metadataTemplate = new StartupMetadataTemplate(namespacePrefix, startups, isDebug);
            var metadataArtifact = metadataTemplate.RenderOutput();
            context.AddSource(metadataArtifact);
        }

        // Generate event handler registration if we have handlers
        if (hasEventHandlers)
        {
            var eventHandlerTemplate = new EventHandlerRegistrationTemplate(eventHandlers, namespacePrefix);
            var eventHandlerArtifact = eventHandlerTemplate.RenderOutput();
            context.AddSource(eventHandlerArtifact);

            // Generate [PragmaticMetadata] for event handlers
            var eventHandlerMetadataTemplate =
                new EventHandlerMetadataTemplate(namespacePrefix, eventHandlers, isDebug);
            var eventHandlerMetadataArtifact = eventHandlerMetadataTemplate.RenderOutput();
            context.AddSource(eventHandlerMetadataArtifact);

            // Generate the AOT-safe typed dispatch table for this assembly's handled events.
            EventDispatchTableEmitter.Emit(context, eventHandlers, namespacePrefix);
        }

        // Detect if this assembly is a package (has IPackageDefinition implementation)
        string? packageRoutePrefix = null;
        try
        {
            packageRoutePrefix = DetectPackageRoutePrefix(compilation);
        }
        catch
        {
            // Detection is best-effort — don't crash the SG if something goes wrong
        }

        // Generate module metadata (always - either from [Module] or auto-generated)
        var moduleToUse = !modules.IsDefaultOrEmpty
            ? modules[0] with { PackageRoutePrefix = packageRoutePrefix }
            : CreateImplicitModule(compilation, namespacePrefix) with { PackageRoutePrefix = packageRoutePrefix };

        // Only generate if there's something to register (services, startups, event handlers, or explicit module)
        if (hasServices || hasStartups || hasEventHandlers || !modules.IsDefaultOrEmpty)
        {
            var moduleMetadataTemplate =
                new ModuleMetadataTemplate(moduleToUse, namespacePrefix, hasServices, hasStartups, isDebug);
            var moduleMetadataArtifact = moduleMetadataTemplate.RenderOutput();
            context.AddSource(moduleMetadataArtifact);
        }
    }

    /// <summary>
    ///     Creates an implicit module when no [Module] attribute is present.
    /// </summary>
    private static ModuleModel CreateImplicitModule(Compilation compilation, string namespacePrefix)
    {
        var assemblyName = compilation.AssemblyName ?? namespacePrefix;

        return new ModuleModel
        {
            Name = assemblyName,
            FullTypeName = $"global::{assemblyName}.ImplicitModule",
            Namespace = namespacePrefix,
            DependsOn = ImmutableArray<string>.Empty,
            Version = null,
            Description = null,
            LocationInfo = null,
            AssemblyName = assemblyName
        };
    }

    private static string DeriveNamespacePrefix(
        ImmutableArray<ModuleModel> modules,
        ImmutableArray<StartupModel> startups,
        ImmutableArray<ServiceModel> services,
        ImmutableArray<DecoratorModel> decorators,
        ImmutableArray<EventHandlerModel> eventHandlers = default)
    {
        // Collect all namespaces from all sources
        var allNamespaces = new List<string>();

        foreach (var module in modules)
            if (!string.IsNullOrEmpty(module.Namespace))
                allNamespaces.Add(module.Namespace);

        foreach (var startup in startups)
            if (!string.IsNullOrEmpty(startup.Namespace))
                allNamespaces.Add(startup.Namespace);

        foreach (var service in services)
            if (!string.IsNullOrEmpty(service.Namespace))
                allNamespaces.Add(service.Namespace);

        foreach (var decorator in decorators)
            if (!string.IsNullOrEmpty(decorator.Namespace))
                allNamespaces.Add(decorator.Namespace);

        if (!eventHandlers.IsDefaultOrEmpty)
            foreach (var handler in eventHandlers)
                if (!string.IsNullOrEmpty(handler.Namespace))
                    allNamespaces.Add(handler.Namespace);

        if (allNamespaces.Count == 0)
            return string.Empty;

        return NamespacePrefixHelper.DerivePrefix(allNamespaces);
    }

    /// <summary>
    ///     Detects if this assembly implements IPackageDefinition and reads RoutePrefix.
    ///     Works because the implementation is in source (same compilation).
    /// </summary>
    private static string? DetectPackageRoutePrefix(Compilation compilation)
    {
        // Pure syntax scan — no semantic model needed (avoids issues with transitive refs)
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var root = syntaxTree.GetRoot();

            foreach (var classDecl in root.DescendantNodes()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>())
            {
                // Quick check: base list contains "IPackageDefinition"
                if (classDecl.BaseList is null) continue;
                var hasPackageDef = false;
                foreach (var baseType in classDecl.BaseList.Types)
                {
                    if (baseType.Type.ToString().Contains("IPackageDefinition"))
                    {
                        hasPackageDef = true;
                        break;
                    }
                }
                if (!hasPackageDef) continue;

                // Found it — read RoutePrefix property from syntax
                foreach (var member in classDecl.Members)
                {
                    if (member is not Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax propDecl)
                        continue;
                    if (propDecl.Identifier.Text != "RoutePrefix") continue;

                    var text = propDecl.ToString();
                    var quoteStart = text.IndexOf('"');
                    if (quoteStart >= 0)
                    {
                        var quoteEnd = text.IndexOf('"', quoteStart + 1);
                        if (quoteEnd > quoteStart)
                            return text.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                    }
                }
            }
        }

        return null;
    }

    private static bool IsDebugConfiguration(Compilation compilation)
    {
        return compilation.Options.OptimizationLevel == OptimizationLevel.Debug;
    }
}
