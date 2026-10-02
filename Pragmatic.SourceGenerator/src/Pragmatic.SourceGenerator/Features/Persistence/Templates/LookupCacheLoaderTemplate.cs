using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates a per-entity ILookupCacheLoader implementation for a [Lookup] entity.
///     The loader queries all records from the DB and populates the in-memory cache at startup.
/// </summary>
internal sealed class LookupCacheLoaderTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;
    private readonly string _loaderClassName;

    public LookupCacheLoaderTemplate(EntityMetadataModel model)
    {
        _model = model;
        _loaderClassName = NamingHelper.AppendSuffix(_model.TypeName, "LookupCacheLoader");
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/LookupLoader";
    protected override string? SourceInfo => $"LookupCacheLoader for {_model.TypeName}";
    protected override string? TriggerInfo => $"[Lookup] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "LookupCacheLoader", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model is { IsValid: true, IsLookup: true };

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq");
        AddUsing("System.Threading");
        AddUsing("System.Threading.Tasks");
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Microsoft.Extensions.DependencyInjection");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        var entityType = $"global::{_model.FullTypeName}";
        var idType = GetFullIdType(_model.IdType);

        XmlSummary($"Preloads all {_model.TypeName} records into the in-memory lookup cache at startup.");

        Class(_loaderClassName, () => RenderBody(entityType, idType),
            interfaces: ["global::Pragmatic.Persistence.EFCore.Repository.ILookupCacheLoader"],
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody(string entityType, string idType)
    {
        var cacheType = $"global::Pragmatic.Persistence.EFCore.Repository.LookupCache<{entityType}, {idType}>";
        var cacheInterfaceType = $"global::Pragmatic.Persistence.Entity.ILookupCache<{entityType}, {idType}>";

        XmlSummary("Loads all records from the database into the cache and registers with the resolver.");
        Method("LoadAsync", () =>
        {
            // ⚠️ Once per tenant when the entity is tenant-scoped, once under no tenant when it is
            // not. Loaded exactly like the other — at startup, with no tenant, against a Set<T>() whose
            // filter fails closed — a tenant-scoped lookup would load zero rows, and zero rows looks
            // like a lookup table nobody has filled yet. The helper is also where
            // "the store knows no tenants" is said out loud instead of passing for success.
            AppendLine($"var tenants = await global::Pragmatic.Persistence.EFCore.Repository.LookupPreload"
                       + $".TenantsToLoadAsync(serviceProvider, {(_model.IsTenantEntity ? "true" : "false")}, "
                       + $"\"{_model.TypeName}\", ct).ConfigureAwait(false);");
            AppendLine();
            AppendLine("foreach (var tenantId in tenants)");
            Block(() => AppendLine("await LoadOneAsync(serviceProvider, tenantId, ct).ConfigureAwait(false);"));
        },
        "global::System.Threading.Tasks.Task",
        [
            new("global::System.IServiceProvider", "serviceProvider"),
            new("global::System.Threading.CancellationToken", "ct")
        ],
        modifiers: new MethodModifiers { IsAsync = true });

        RenderLoadForTenant();
        RenderLoadOne(entityType, idType, cacheType, cacheInterfaceType);
    }

    /// <summary>
    ///     The reaction to a tenant that appeared after startup — and, for a lookup that is not
    ///     tenant-scoped, nothing at all.
    /// </summary>
    /// <remarks>
    ///     Whether this lookup has one cache for the whole process or one per tenant is a property of
    ///     the entity, known here. Emitting the load and letting it discover at runtime that there is
    ///     nothing per-tenant to do would put a branch in every application that has no tenants.
    /// </remarks>
    private void RenderLoadForTenant()
    {
        XmlSummary("Loads the cache for one tenant that appeared while the host was running.");
        Method("LoadForTenantAsync", () =>
        {
            if (_model.IsTenantEntity)
            {
                AppendLine("return LoadOneAsync(serviceProvider, tenantId, ct);");
            }
            else
            {
                Comment("Not tenant-scoped: one cache for the whole process, loaded once at startup");
                AppendLine("return global::System.Threading.Tasks.Task.CompletedTask;");
            }
        },
        "global::System.Threading.Tasks.Task",
        [
            new("global::System.IServiceProvider", "serviceProvider"),
            new("string", "tenantId"),
            new("global::System.Threading.CancellationToken", "ct")
        ]);
    }

    private void RenderLoadOne(string entityType, string idType, string cacheType, string cacheInterfaceType)
    {
        XmlSummary("Loads the cache for a single tenant, or for no tenant at all when it is null.");
        Method("LoadOneAsync", () =>
        {
            // The tenant is declared, not passed: the filter reads ITenantContext, and the scope
            // below has to be created inside it so the DbContext resolves under the right one.
            AppendLine("using var tenantScope = tenantId is null");
            AppendLine("    ? null");
            AppendLine("    : global::Pragmatic.MultiTenancy.TenantScope.BeginScope(tenantId);");
            AppendLine();
            AppendLine("using var scope = serviceProvider.CreateScope();");

            // Resolve DbContext — use keyed DI if boundary is known, otherwise resolve directly
            if (!string.IsNullOrEmpty(_model.BoundaryTypeFullName))
            {
                var boundaryType = _model.BoundaryTypeFullName!.StartsWith("global::", StringComparison.Ordinal)
                    ? _model.BoundaryTypeFullName
                    : $"global::{_model.BoundaryTypeFullName}";
                AppendLine($"var db = scope.ServiceProvider.GetRequiredKeyedService<global::Microsoft.EntityFrameworkCore.DbContext>(typeof({boundaryType}));");
            }
            else
            {
                AppendLine("var db = scope.ServiceProvider.GetRequiredService<global::Microsoft.EntityFrameworkCore.DbContext>();");
            }

            AppendLine($"var items = await db.Set<{entityType}>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);");
            AppendLine();
            // ⚠️ The registered singleton when the lookup is shared, a fresh cache per tenant when
            // it is not. Loading a second tenant into the singleton would overwrite the first,
            // which is the process-wide sharing this exists to end — but a shared lookup has
            // exactly one cache, and it is the one an application injects as
            // ILookupCache<T, TId>. Filling a copy instead left that injection empty, which is
            // how the Showcase's own lookup test found this.
            AppendLine($"var cache = tenantId is null");
            AppendLine($"    ? ({cacheType})serviceProvider.GetRequiredService<{cacheInterfaceType}>()");
            AppendLine($"    : new {cacheType}();");
            AppendLine($"cache.Load(items, e => e.PersistenceId);");
            AppendLine();
            Comment("Register with the ambient resolver, under the tenant just declared");
            AppendLine($"global::Pragmatic.Persistence.Entity.LookupResolver.Register<{entityType}, {idType}>(cache);");
        },
        "global::System.Threading.Tasks.Task",
        [
            new("global::System.IServiceProvider", "serviceProvider"),
            new("string?", "tenantId"),
            new("global::System.Threading.CancellationToken", "ct")
        ],
        accessModifier: AccessModifier.Private,
        modifiers: new MethodModifiers { IsAsync = true });
    }

    private static string GetFullIdType(string idType)
    {
        if (idType.StartsWith("global::"))
            return idType;
        return $"global::{idType}";
    }
}
