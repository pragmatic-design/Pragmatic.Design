using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;
using Pragmatic.Notifications.Slack;
using Pragmatic.Notifications.Sms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Notifications.Tests;

/// <summary>
///     The one way to turn each channel package on.
/// </summary>
/// <remarks>
///     <c>AddSlack</c> and <c>AddTwilioSms</c> are the only entry points their packages have, and
///     nothing called them, no test ran them and no page named them. A channel that fails to register
///     does not throw — the notification is simply delivered by nobody.
/// </remarks>
public class ChannelPackageEntryPointsTests
{
    [Fact]
    public void AddSlack_RegistersADeliveryChannel()
    {
        var services = new ServiceCollection();

        services.AddPragmaticNotifications(b => b.AddSlack(o => o.DefaultWebhookUrl = "https://hooks.slack.com/services/x"));

        services.Should().Contain(d => d.ServiceType == typeof(INotificationChannel));
    }

    [Fact]
    public void AddTwilioSms_RegistersADeliveryChannel()
    {
        var services = new ServiceCollection();

        services.AddPragmaticNotifications(b => b.AddTwilioSms(o =>
        {
            o.AccountSid = "AC0";
            o.AuthToken = "token";
            o.FromNumber = "+10000000000";
        }));

        services.Should().Contain(d => d.ServiceType == typeof(INotificationChannel));
    }

    /// <remarks>
    ///     Registered as enumerable, so adding a second channel never displaces the first — which is
    ///     the whole point of having more than one way to reach someone.
    /// </remarks>
    [Fact]
    public void BothChannels_Coexist()
    {
        var services = new ServiceCollection();

        services.AddPragmaticNotifications(b =>
        {
            b.AddSlack(o => o.DefaultWebhookUrl = "https://hooks.slack.com/services/x");
            b.AddTwilioSms(o =>
            {
                o.AccountSid = "AC0";
                o.AuthToken = "token";
                o.FromNumber = "+10000000000";
            });
        });

        services.Count(d => d.ServiceType == typeof(INotificationChannel)).Should().Be(2);
    }
}
