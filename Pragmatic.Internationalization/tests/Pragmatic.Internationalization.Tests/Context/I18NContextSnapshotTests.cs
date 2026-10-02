using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Context;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Context;

[Collection("I18nContext")]
public class I18NContextSnapshotTests : IDisposable
{
    public I18NContextSnapshotTests() => I18NContext.Clear();

    public void Dispose() => I18NContext.Clear();

    [Fact]
    public void Capture_RecordsCurrentContextAndThreadCultures()
    {
        I18NContext.SetCulture("it-IT");

        var snapshot = I18NContext.Capture();

        snapshot.Context.Should().NotBeNull();
        snapshot.Context!.CultureCode.Should().Be("it-IT");
        snapshot.ThreadCulture.Should().NotBeNull();
        snapshot.ThreadUICulture.Should().NotBeNull();
    }

    [Fact]
    public void Restore_AfterMutation_RevertsContext()
    {
        I18NContext.SetCulture("fr-FR");
        var snapshot = I18NContext.Capture();

        I18NContext.SetCulture("ja-JP");
        I18NContext.Current.CultureCode.Should().Be("ja-JP");

        I18NContext.Restore(snapshot);

        I18NContext.Current.CultureCode.Should().Be("fr-FR");
    }

    [Fact]
    public void Restore_SnapshotWithNoContext_ClearsCurrent()
    {
        I18NContext.Clear();
        var snapshot = I18NContext.Capture();

        I18NContext.SetCulture("de-DE");

        I18NContext.Restore(snapshot);

        // Snapshot captured the (no-explicit-context) state; restoring reverts it.
        I18NContext.Current.CultureCode.Should().NotBe("de-DE");
    }

    [Fact]
    public void CaptureRestore_RoundTrips_ViaI18NShortcut()
    {
        I18NContext.SetCulture("es-ES");
        var snapshot = I18N.Capture();

        I18N.SetCulture("pt-BR");
        I18N.Restore(snapshot);

        I18N.CultureCode.Should().Be("es-ES");
    }
}
