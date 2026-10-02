using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Logging.Configuration;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
using Xunit;

namespace Pragmatic.Logging.Tests.Configuration;

public class CompliancePresetTests
{
    // UseCompliancePreset must pick an implemented audit storage: the audit-storage factory rejects
    // Audit.StorageType = "Database" with a NotSupportedException at DI resolution, so a preset that
    // chose it would crash every application that enabled it.
    [Theory]
    [InlineData(ComplianceStandard.Gdpr)]
    [InlineData(ComplianceStandard.Hipaa)]
    [InlineData(ComplianceStandard.PciDss)]
    public void UseCompliancePreset_UsesAnImplementedAuditStorage(ComplianceStandard standard)
    {
        var services = new ServiceCollection();
        services.AddPragmaticLogging(b => b.UseCompliancePreset(standard));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PragmaticLoggingOptions>>().Value;

        options.Audit.Enabled.Should().BeTrue();
        // Only "filesystem" and "memory" are implemented; "database" throws.
        options.Audit.StorageType.ToLowerInvariant().Should().BeOneOf("filesystem", "memory");
    }

    /// <summary>
    ///     The data redaction and audit settings the one builder carries reach the options, as they did
    ///     on the builder they came from.
    /// </summary>
    [Fact]
    public void EnableDataRedactionAndAuditTrail_ReachTheOptions()
    {
        var services = new ServiceCollection();
        services.AddPragmaticLogging(b => b
            .EnableDataRedaction(r => r.PreserveLengths = true)
            .EnableAuditTrail(a => a.BatchSize = 7));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PragmaticLoggingOptions>>().Value;

        options.Privacy.EnableRedaction.Should().BeTrue();
        options.Privacy.DataRedaction.PreserveLengths.Should().BeTrue();
        options.Audit.Enabled.Should().BeTrue();
        options.Audit.BatchSize.Should().Be(7, "not the default of 100: the configure action ran");
    }

    /// <summary>
    ///     One builder: with two public <c>PragmaticLoggingBuilder</c> classes, reached from two entry
    ///     points with different <c>Add*</c> overloads, which one a reader got would depend on a
    ///     <c>using</c>.
    /// </summary>
    [Fact]
    public void ThePackage_HasOneLoggingBuilder()
        => typeof(PragmaticLoggingOptions).Assembly.GetTypes()
            .Count(t => t.Name == "PragmaticLoggingBuilder").Should().Be(1);
}
