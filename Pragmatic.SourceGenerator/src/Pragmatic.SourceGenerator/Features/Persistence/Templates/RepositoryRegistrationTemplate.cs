using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Emits <c>_Infra.Persistence.RepositoryRegistration.g.cs</c> with
///     <c>AddPragmaticPersistenceRepositories&lt;TDbContext&gt;()</c>.
/// </summary>
internal sealed class RepositoryRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<EntityMetadataModel> _entities;

    public RepositoryRegistrationTemplate(ImmutableArray<EntityMetadataModel> entities)
        => _entities = entities;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? TriggerInfo => $"{_entities.Length} entity repository(ies)";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Persistence", "RepositoryRegistration"),
        ToSourceText());

    // Emit only when the assembly has at least one local [Entity] — otherwise the helper
    // would be an empty shell.
    protected override bool Validate() => _entities.Any(e => e.IsValid && !e.IsFromReference);

    public override void RenderFile()
    {
        AppendLine("// Pragmatic.Persistence — library-mode repository registration");
        AppendLine("#nullable enable");
        AppendLine();
        AppendLine("namespace Pragmatic.Persistence.Generated;");
        AppendLine();
        AppendLine("public static partial class PragmaticPersistenceRegistration");
        AppendLine("{");
        AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddPragmaticPersistenceRepositories<TDbContext>(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        AppendLine("        where TDbContext : global::Microsoft.EntityFrameworkCore.DbContext");
        AppendLine("    {");

        var validEntities = _entities
            .Where(e => e.IsValid && !e.IsFromReference)
            .OrderBy(e => e.FullTypeName)
            .ToList();

        foreach (var entity in validEntities)
        {
            var entityFqn = $"global::{entity.FullTypeName}";
            var repoFqn = $"{entityFqn}.Repository";
            var idFqn = FormatIdType(entity.IdType);
            var keyedType = GetKeyedServiceType(entity, repoFqn);

            AppendLine($"        // {entity.TypeName}");
            AppendLine($"        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddKeyedScoped<global::Microsoft.EntityFrameworkCore.DbContext>(services, typeof({keyedType}), (sp, _) => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<TDbContext>(sp));");
            // Same key as the context, so a repository and an invoker resolving it in one scope get the
            // same instance — CommitScope keys on unit-of-work identity, and two instances over one
            // DbContext would read as two boundaries. Host mode registers this in
            // DbContextRegistrationTemplate; library mode has only this file, and without it the
            // repository's required IUnitOfWork would fail to resolve.
            AppendLine($"        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddKeyedScoped<global::Pragmatic.Persistence.Repository.IUnitOfWork>(services, typeof({keyedType}), (sp, _) => new global::Pragmatic.Persistence.EFCore.UnitOfWork.EfCoreUnitOfWork(global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<TDbContext>(sp), global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Microsoft.Extensions.Logging.ILogger<global::Pragmatic.Persistence.EFCore.UnitOfWork.EfCoreUnitOfWork>>(sp), global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Pragmatic.Events.IDomainEventDispatcher>(sp), global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<global::Pragmatic.Persistence.Lifecycle.IGeneratedValueBinding>(sp), global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::System.TimeProvider>(sp), global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Pragmatic.Identity.ICurrentUser>(sp)));");
            AppendLine($"        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<{repoFqn}>(services);");
            AppendLine($"        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<global::Pragmatic.Persistence.Repository.IRepository<{entityFqn}>>(services, sp => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{repoFqn}>(sp));");
            AppendLine($"        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<global::Pragmatic.Persistence.Repository.IReadRepository<{entityFqn}>>(services, sp => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{repoFqn}>(sp));");
        }

        AppendLine();
        AppendLine("        // Roll-up rules (#2) — implemented by the RollUp feature when [RollUp] is present; otherwise a no-op.");
        AppendLine("        RegisterRollUpRules(services);");
        AppendLine("        return services;");
        AppendLine("    }");
        AppendLine();
        AppendLine("    static partial void RegisterRollUpRules(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services);");
        AppendLine("}");
    }

    private static string GetKeyedServiceType(EntityMetadataModel entity, string repoFqn)
    {
        if (!string.IsNullOrEmpty(entity.BoundaryTypeFullName))
        {
            return entity.BoundaryTypeFullName!.StartsWith("global::", System.StringComparison.Ordinal)
                ? entity.BoundaryTypeFullName
                : $"global::{entity.BoundaryTypeFullName}";
        }
        return repoFqn;
    }

    private static string FormatIdType(string idType)
    {
        return idType switch
        {
            "int" or "long" or "string" => idType,
            _ when idType.Contains(".") => $"global::{idType}",
            _ => idType
        };
    }
}
