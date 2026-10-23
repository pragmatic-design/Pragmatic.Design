using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Templates;

/// <summary>
///     The persistence of a boundary an engine serves, registered as Pragmatic registers a boundary's: the unit of work keyed by
///     <c>typeof(TBoundary)</c>, its session with it, and each entity's <c>IRepository&lt;T&gt;</c> and <c>IReadRepository&lt;T&gt;</c> unkeyed (an
///     entity has one boundary). The session is the host's to open; the filters are built here from the services Pragmatic registers.
/// </summary>
internal sealed class BoundaryPersistenceTemplate(BoundaryPersistenceModel model) : CSharpTemplate
{
    private const string ServiceCollection = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string Services = "global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions";
    private const string Keyed = "global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions";

    protected override string? GeneratorName => "Pragmatic.Persistence.Sql";

    protected override string? SourceInfo => $"the persistence of {model.BoundaryName}";

    public override Artifact RenderOutput() => new(model.Hint, ToSourceText());

    public override void RenderFile()
    {
        var key = $"typeof({model.BoundaryType})";
        var e = model.Engine;
        var name = model.BoundaryName;
        if (model.Namespace is not null)
            AppendLine($"namespace {model.Namespace};");
        AppendLine();
        AppendLine($"/// <summary>The persistence of {name}: its unit of work keyed by its type, its entities' repositories.</summary>");
        AppendLine($"public static class {name}Persistence");
        AppendLine("{");
        AppendLine("    /// <summary>");
        AppendLine($"    ///     Registers {name}'s unit of work (keyed by <c>typeof({name})</c>) and its entities' repositories. <paramref name=\"session\" />");
        AppendLine("    ///     opens the boundary's session with the filters built from the request's services.");
        AppendLine("    /// </summary>");
        AppendLine($"    public static {ServiceCollection} Add{name}Persistence(this {ServiceCollection} services, global::System.Func<global::System.IServiceProvider, {e.Filters}, {e.Session}> session)");
        AppendLine("    {");
        AppendLine($"        {Services}.AddKeyedScoped<{e.Session}>(services, {key}, (sp, _) => session(sp, new {e.BridgeFilters}(");
        AppendLine("            Get<global::Pragmatic.Identity.ICurrentUser>(sp), Get<global::Pragmatic.MultiTenancy.ITenantContext>(sp), Get<global::Pragmatic.Authorization.IUserScopeResolver>(sp),");
        AppendLine("            Get<global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle>(sp), Get<global::Pragmatic.Persistence.Query.Filters.QueryFilterOptions>(sp), Get<global::System.TimeProvider>(sp),");
        AppendLine("            All<global::Pragmatic.Persistence.Scopes.DataScopeRule>(sp))));");
        // ASSUMPTION: requireTenant false, as a unit of work built without options; the repo's tenant interceptor option is a porting item.
        AppendLine($"        {Services}.AddKeyedScoped<{e.UnitOfWork}>(services, {key}, (sp, k) => new {e.UnitOfWork}(");
        AppendLine($"            {Keyed}.GetRequiredKeyedService<{e.Session}>(sp, k), Get<global::System.TimeProvider>(sp),");
        AppendLine("            Get<global::Pragmatic.Identity.ICurrentUser>(sp), Get<global::Pragmatic.MultiTenancy.ITenantContext>(sp), false, All<global::Pragmatic.Persistence.Lifecycle.IGeneratedValueBinding>(sp),");
        AppendLine($"            Get<global::Pragmatic.Events.IDomainEventDispatcher>(sp), All<{e.Phase}>(sp), All<global::Pragmatic.Persistence.Scopes.DataScopeRule>(sp)));");
        AppendLine($"        {Services}.AddKeyedScoped<global::Pragmatic.Persistence.Repository.IUnitOfWork>(services, {key},");
        AppendLine($"            (sp, k) => {Keyed}.GetRequiredKeyedService<{e.UnitOfWork}>(sp, k));");
        foreach (var type in model.EntityTypes)
        {
            AppendLine($"        {Services}.AddScoped<global::Pragmatic.Persistence.Repository.IRepository<{type}>>(services,");
            AppendLine($"            sp => new {type}.{e.Repository}({Keyed}.GetRequiredKeyedService<{e.UnitOfWork}>(sp, {key})));");
            AppendLine($"        {Services}.AddScoped<global::Pragmatic.Persistence.Repository.IReadRepository<{type}>>(services,");
            AppendLine($"            sp => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Pragmatic.Persistence.Repository.IRepository<{type}>>(sp));");
        }

        AppendLine("        return services;");
        AppendLine("    }");
        AppendLine();
        AppendLine("    private static T? Get<T>(global::System.IServiceProvider sp) where T : class => (T?)sp.GetService(typeof(T));");
        AppendLine();
        AppendLine("    private static global::System.Collections.Generic.IEnumerable<T> All<T>(global::System.IServiceProvider sp)");
        AppendLine("        => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<T>(sp);");
        AppendLine("}");
    }
}
