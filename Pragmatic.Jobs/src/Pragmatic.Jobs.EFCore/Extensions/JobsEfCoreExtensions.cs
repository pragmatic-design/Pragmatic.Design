using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Jobs.Configuration;

namespace Pragmatic.Jobs.EFCore.Extensions;

/// <summary>
///     EF Core extensions for <see cref="JobsBuilder"/>.
/// </summary>
public static class JobsEfCoreExtensions
{
    /// <summary>
    ///     Replaces InMemory stores with EF Core-backed persistence.
    ///     Requires <c>JobEntityTypeConfiguration</c> and <c>RecurringJobEntityTypeConfiguration</c>
    ///     applied to your DbContext.
    /// </summary>
    public static JobsBuilder UseEfCorePersistence(this JobsBuilder builder)
    {
        builder.Services.RemoveAll<IJobStore>();
        builder.Services.RemoveAll<IRecurringJobStore>();
        builder.Services.AddScoped<IJobStore, EfCoreJobStore>();
        builder.Services.AddScoped<IRecurringJobStore, EfCoreRecurringJobStore>();
        return builder;
    }
}
