using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Pragmatic.Temporal.EntityFrameworkCore;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>
///     The three activation paths (UsePragmaticTemporal, ApplyTemporalConventions,
///     ConfigureConventions + TemporalModelConvention) must produce identical models —
///     including the CronExpression nullable split, where the paths can diverge.
/// </summary>
public class ManualPathParityTests
{
    private static Dictionary<string, (string? Converter, int? MaxLength)> Snapshot(IModel model)
    {
        var entityType = model.FindEntityType(typeof(TemporalEntity))!;
        return entityType.GetProperties().ToDictionary(
            p => p.Name,
            p => (p.GetValueConverter()?.GetType().FullName, p.GetMaxLength()));
    }

    [Fact]
    public void ManualApply_MatchesUsePragmaticTemporal()
    {
        var (autoConn, autoOptions) = SqliteContextFactory.CreateOptions<AutoTemporalContext>(
            b => b.UsePragmaticTemporal());
        var (manualConn, manualOptions) = SqliteContextFactory.CreateOptions<ManualApplyContext>();
        using var _ = autoConn;
        using var __ = manualConn;

        using var autoContext = new AutoTemporalContext(autoOptions);
        using var manualContext = new ManualApplyContext(manualOptions);

        Assert.Equal(Snapshot(autoContext.Model), Snapshot(manualContext.Model));
    }

    [Fact]
    public void ConventionsAdd_MatchesUsePragmaticTemporal()
    {
        var (autoConn, autoOptions) = SqliteContextFactory.CreateOptions<AutoTemporalContext>(
            b => b.UsePragmaticTemporal());
        var (convConn, convOptions) = SqliteContextFactory.CreateOptions<ConventionsAddContext>();
        using var _ = autoConn;
        using var __ = convConn;

        using var autoContext = new AutoTemporalContext(autoOptions);
        using var convContext = new ConventionsAddContext(convOptions);

        Assert.Equal(Snapshot(autoContext.Model), Snapshot(convContext.Model));
    }
}
