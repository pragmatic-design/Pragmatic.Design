using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Temporal.EntityFrameworkCore;

/// <summary>
///     EF Core extension for Pragmatic.Temporal options.
///     Registers the convention set plugin that applies temporal value converters.
/// </summary>
internal sealed class TemporalOptionsExtension(TemporalEfCoreOptions options) : IDbContextOptionsExtension
{
    public TemporalEfCoreOptions Options { get; } = options;

    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, TemporalConventionSetPlugin>();
    }

    public void Validate(IDbContextOptions options)
    {
        // Nothing to validate: both options are plain booleans.
    }

    private sealed class ExtensionInfo(TemporalOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private new TemporalOptionsExtension Extension => (TemporalOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "PragmaticTemporal";

        public override int GetServiceProviderHashCode()
        {
            // The plugin reads options per context from IDbContextOptions, so the
            // registered services do not vary with option values.
            return 0;
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is ExtensionInfo;
        }

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["PragmaticTemporal:ApplyToAllProperties"] =
                Extension.Options.ApplyToAllProperties ? "1" : "0";
            debugInfo["PragmaticTemporal:StoreDurationAsTicks"] =
                Extension.Options.StoreDurationAsTicks ? "1" : "0";
        }
    }
}
