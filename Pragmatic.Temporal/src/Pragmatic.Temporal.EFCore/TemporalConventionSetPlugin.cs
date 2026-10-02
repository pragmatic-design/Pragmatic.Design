using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Pragmatic.Temporal.EntityFrameworkCore.Conventions;

namespace Pragmatic.Temporal.EntityFrameworkCore;

/// <summary>
///     Convention set plugin that wires <see cref="TemporalModelConvention" /> into the
///     model-building pipeline when <c>UsePragmaticTemporal()</c> is configured.
///     Options are read from the current context's <see cref="IDbContextOptions" />
///     (same pattern as the NetTopologySuite plugin).
/// </summary>
internal sealed class TemporalConventionSetPlugin(IDbContextOptions dbContextOptions) : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        var options = dbContextOptions.FindExtension<TemporalOptionsExtension>()?.Options
                      ?? new TemporalEfCoreOptions();

        if (options.ApplyToAllProperties)
        {
            var convention = new TemporalModelConvention(options);
            conventionSet.ModelInitializedConventions.Add(convention);
            conventionSet.EntityTypeAddedConventions.Add(convention);
            conventionSet.ModelFinalizingConventions.Add(convention);
        }

        return conventionSet;
    }
}
