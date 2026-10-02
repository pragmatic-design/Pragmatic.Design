using Pragmatic.Testing.Assertions;
using Pragmatic.Notifications.Preferences;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class DefaultPreferenceProviderTests
{
    private readonly DefaultPreferenceProvider _provider = new();

    [Fact]
    public async Task GetPreferencesAsync_ReturnsEnabledDefaults()
    {
        var prefs = await _provider.GetPreferencesAsync("user@example.com");

        prefs.Should().NotBeNull();
        prefs!.Enabled.Should().BeTrue();
        prefs.DoNotDisturb.Should().BeFalse();
        prefs.MutedCategories.Should().BeNull();
        prefs.PreferredChannel.Should().Be(NotificationChannel.Email);
    }

    [Fact]
    public async Task GetPreferencesAsync_ForDifferentUsers_ReturnsSameDefault()
    {
        var a = await _provider.GetPreferencesAsync("a@example.com");
        var b = await _provider.GetPreferencesAsync("b@example.com");

        a.Should().BeSameAs(b);
    }
}
