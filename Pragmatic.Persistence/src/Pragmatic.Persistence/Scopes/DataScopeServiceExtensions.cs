using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Filters;

namespace Pragmatic.Persistence.Scopes;

/// <summary>
///     DI registration extensions for data scope rules.
/// </summary>
public static class DataScopeServiceExtensions
{
    /// <summary>
    ///     Registers a <see cref="DataScopeRule{T}"/> and its associated runtime filter
    ///     if the rule strategy includes computed evaluation.
    /// </summary>
    /// <typeparam name="TRule">The data scope rule type.</typeparam>
    /// <typeparam name="T">The entity type.</typeparam>
    public static IServiceCollection AddDataScopeRule<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRule, T>(this IServiceCollection services)
        where TRule : DataScopeRule<T>
        where T : class, IScopedEntity
    {
        // Register the rule itself (singleton — rules are stateless expression factories), under both shapes and each from its own type.
        // The untyped shape must not resolve the typed one: GetRequiredService<DataScopeRule<T>>() answers the last rule registered for T,
        // so with two rules on one entity the engines, which read IEnumerable<DataScopeRule>, saw the second twice (#597).
        services.AddSingleton<DataScopeRule<T>, TRule>();
        services.AddSingleton<DataScopeRule, TRule>();

        // TryAddEnumerable, never TryAddScoped: IQueryFilter is resolved as a collection, and TryAdd
        // asks whether *any* IQueryFilter is registered — soft-delete or tenant always got there
        // first, so this line skipped every time and the computed rule silently never filtered.
        // TryAddEnumerable compares the implementation type instead, which is the intended guard.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IQueryFilter, ComputedScopeFilter<T>>());

        // Register in static registry for compile-time-safe discovery
        ScopeRegistry.Register<T>(typeof(TRule));

        return services;
    }
}
