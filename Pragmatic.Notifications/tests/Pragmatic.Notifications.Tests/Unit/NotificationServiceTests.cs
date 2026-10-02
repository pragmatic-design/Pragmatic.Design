using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications.Extensions;
using Pragmatic.Notifications.Testing;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class NotificationServiceTests
{
    [Fact]
    public void AddPragmaticNotifications_RegistersAllServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticNotifications();

        var sp = services.BuildServiceProvider();

        sp.GetService<INotificationService>().Should().NotBeNull();
    }

    [Fact]
    public void AddNotificationTestHarness_ReplacesService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticNotifications();
        var harness = services.AddNotificationTestHarness();

        var sp = services.BuildServiceProvider();
        var service = sp.GetRequiredService<INotificationService>();

        service.Should().BeSameAs(harness);
    }

    [Fact]
    public async Task EnqueueAsync_WritesToChannel()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticNotifications();
        var sp = services.BuildServiceProvider();

        var service = sp.GetRequiredService<INotificationService>();
        var request = new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Direct("test@example.com"),
            Content = new NotificationContent { Subject = "Test", Body = "Body" },
        };

        var result = await service.EnqueueAsync(request);

        result.Success.Should().BeTrue();
        result.NotificationId.Should().NotBeEmpty();
    }
}
