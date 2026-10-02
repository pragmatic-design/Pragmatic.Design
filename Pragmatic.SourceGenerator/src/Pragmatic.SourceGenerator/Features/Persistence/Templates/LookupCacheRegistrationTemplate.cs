using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates DI registration and a hosted loader for all [Lookup] entities in the assembly.
///     Registers ILookupCache&lt;T, TId&gt; as singleton + an IHostedService that preloads at startup.
/// </summary>
internal sealed class LookupCacheRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<EntityMetadataModel> _lookups;

    public LookupCacheRegistrationTemplate(ImmutableArray<EntityMetadataModel> lookups) =>
        _lookups = lookups;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/LookupRegistration";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForAssembly("Persistence", "LookupCache"),
            ToSourceText());
    }

    protected override bool Validate() => _lookups.Length > 0;

    /// <summary>
    ///     The fully qualified name of the registration method this template emits, or <c>null</c> when
    ///     the assembly declares no <c>[Lookup]</c>.
    /// </summary>
    /// <remarks>
    ///     Written into the assembly's Persistence metadata so the host can call it. The host does not
    ///     call <c>Add{Prefix}LookupCaches()</c> — it builds its container from the metadata documents —
    ///     so the method was generated and invoked by nothing, and the documentation had turned that
    ///     into an instruction for the reader ("call it yourself, even inside a host"). One application
    ///     followed the instruction; every other one got no <c>ILookupCache</c> in the container, no
    ///     loader, and no preload: a nullable lookup navigation quietly returned <c>null</c> and a
    ///     non-nullable one threw on every read.
    /// </remarks>
    public static string? RegistrationMethodFor(ImmutableArray<EntityMetadataModel> lookups)
    {
        if (lookups.IsDefaultOrEmpty)
            return null;

        var ns = ContainerNamespace(lookups);
        var className = ClassNameFor(lookups);
        var container = string.IsNullOrEmpty(ns) ? className : $"{ns}.{className}";

        return $"{container}.{MethodNameFor(lookups)}";
    }

    private static string IdentifierPrefix(ImmutableArray<EntityMetadataModel> lookups)
        => NamespacePrefixHelper.ToIdentifier(
            NamespacePrefixHelper.DerivePrefix(
                lookups.Select(l => l.Namespace).Where(n => !string.IsNullOrEmpty(n))));

    private static string ClassNameFor(ImmutableArray<EntityMetadataModel> lookups)
    {
        var prefix = IdentifierPrefix(lookups);
        return string.IsNullOrEmpty(prefix)
            ? "LookupCacheRegistrationExtensions"
            : $"{prefix}LookupCacheRegistrationExtensions";
    }

    private static string MethodNameFor(ImmutableArray<EntityMetadataModel> lookups)
    {
        var prefix = IdentifierPrefix(lookups);
        return string.IsNullOrEmpty(prefix) ? "AddLookupCaches" : $"Add{prefix}LookupCaches";
    }

    private static string ContainerNamespace(ImmutableArray<EntityMetadataModel> lookups)
    {
        var ns = lookups
            .Select(l => l.Namespace)
            .FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";

        // Go to parent namespace if it ends with ".Entities"
        return ns.EndsWith(".Entities", StringComparison.Ordinal)
            ? ns.Substring(0, ns.Length - ".Entities".Length)
            : ns;
    }

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq");
        AddUsing("System.Threading");
        AddUsing("System.Threading.Tasks");
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Microsoft.Extensions.Hosting");

        // Namespace and class name come from the same helpers RegistrationMethodFor uses, so the name
        // written into the metadata and the name emitted here cannot drift apart.
        AppendNamespace(ContainerNamespace(_lookups));
        AppendLine();

        var className = ClassNameFor(_lookups);

        XmlSummary("Registers lookup caches and the hosted loader for preloading at startup.");
        Class(className, RenderMethods,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderMethods()
    {
        var methodName = MethodNameFor(_lookups);

        XmlSummary("Registers all lookup caches as singletons and adds the hosted loader.");
        var parameters = new List<MethodParameter>
        {
            new("this global::Microsoft.Extensions.DependencyInjection.IServiceCollection", "services")
        };

        Method(methodName, RenderRegistrationBody,
            "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
            parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderRegistrationBody()
    {
        Comment("Register lookup caches as singletons");
        foreach (var lookup in _lookups)
        {
            var entityType = $"global::{lookup.FullTypeName}";
            var idType = GetFullIdType(lookup.IdType);
            var cacheType = $"global::Pragmatic.Persistence.EFCore.Repository.LookupCache<{entityType}, {idType}>";
            var interfaceType = $"global::Pragmatic.Persistence.Entity.ILookupCache<{entityType}, {idType}>";

            AppendLine($"var {ToCamelCase(lookup.TypeName)}Cache = new {cacheType}();");
            AppendLine($"services.AddSingleton<{interfaceType}>({ToCamelCase(lookup.TypeName)}Cache);");
        }

        AppendLine();
        Comment("Register per-entity cache loaders");
        foreach (var lookup in _lookups)
        {
            var loaderClassName = NamingHelper.AppendSuffix(lookup.TypeName, "LookupCacheLoader");
            var loaderType = $"global::{lookup.Namespace}.{loaderClassName}";
            AppendLine($"services.AddSingleton<global::Pragmatic.Persistence.EFCore.Repository.ILookupCacheLoader, {loaderType}>();");
        }

        AppendLine();
        Comment("Register hosted service for preloading caches at startup");
        AppendLine("services.AddHostedService<global::Pragmatic.Persistence.EFCore.Repository.LookupPreloadHostedService>();");

        // Only when a lookup is tenant-scoped. The preload enumerates the tenants known at startup
        // and stops there, so a tenant created afterwards has no cache — but a lookup with one cache
        // for the whole process has nothing to follow, and registering an observer that every
        // transition would call to do nothing is the runtime branch this codebase writes at compile
        // time instead.
        if (_lookups.Any(l => l.IsTenantEntity))
        {
            AppendLine();
            Comment("Follow the tenant lifecycle — a tenant created after startup needs its caches loaded");
            AppendLine("services.AddSingleton<global::Pragmatic.MultiTenancy.ITenantLifecycleObserver, "
                       + "global::Pragmatic.Persistence.EFCore.Repository.LookupCacheTenantObserver>();");
        }

        AppendLine();
        AppendLine("return services;");
    }

    private static string GetFullIdType(string idType)
    {
        if (idType.StartsWith("global::"))
            return idType;
        return $"global::{idType}";
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
