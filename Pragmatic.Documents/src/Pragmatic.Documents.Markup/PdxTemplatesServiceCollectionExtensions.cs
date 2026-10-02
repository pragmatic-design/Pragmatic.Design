using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Internationalization.Providers;

namespace Pragmatic.Documents.Markup;

/// <summary>
///     Registers <see cref="IPdxTemplates" />.
/// </summary>
public static class PdxTemplatesServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="IPdxTemplates" /> over the sources <paramref name="configure" /> adds —
    ///     accumulated across calls, so each module adds its own and the host adds nothing.
    /// </summary>
    /// <example>
    ///     <code>
    ///     services.AddPdxTemplates(t => t.FromAssemblyOf&lt;BillingBoundary&gt;());
    ///     </code>
    /// </example>
    public static IServiceCollection AddPdxTemplates(
        this IServiceCollection services, Action<PdxTemplateSourcesBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // One list for every call: the second module's templates join the first's instead of replacing
        // them, and the order of the calls is the order the sources are asked.
        var registered = services
            .FirstOrDefault(d => d.ServiceType == typeof(PdxTemplateSourceList))?
            .ImplementationInstance as PdxTemplateSourceList;

        if (registered is null)
        {
            registered = new PdxTemplateSourceList();
            services.AddSingleton(registered);
        }

        configure(new PdxTemplateSourcesBuilder(registered.Sources));

        services.TryAddScoped<IPdxTemplates>(sp => new PdxTemplates(
            new FirstFoundPdxTemplateSource([.. sp.GetRequiredService<PdxTemplateSourceList>().Sources]),
            sp.GetRequiredService<IStringLocalizer>()));

        return services;
    }
}
