using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Tests.Unit;

public class MessagingServiceExtensionsTests
{
    [Fact]
    public void AddPragmaticMessaging_ShouldRegisterCoreDependencies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        var sp = services.BuildServiceProvider();

        sp.GetService<IMessageBus>().Should().NotBeNull();
        sp.GetService<IMessageSerializer>().Should().NotBeNull();
        sp.GetService<IDeadLetterStore>().Should().NotBeNull();
        sp.Dispose();
    }

    [Fact]
    public void AddPragmaticMessaging_ShouldRegisterInMemoryBus()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        var sp = services.BuildServiceProvider();

        sp.GetService<IMessageBus>().Should().BeOfType<InMemoryMessageBus>();
        sp.Dispose();
    }

    [Fact]
    public void AddPragmaticMessaging_ShouldRegisterInMemoryDeadLetterStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        var sp = services.BuildServiceProvider();

        sp.GetService<IDeadLetterStore>().Should().BeOfType<InMemoryDeadLetterStore>();
        sp.GetService<InMemoryDeadLetterStore>().Should().NotBeNull();
        sp.Dispose();
    }

    [Fact]
    public void AddPragmaticMessaging_WithOutboxEnabled_ShouldSetOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging(msg =>
        {
            msg.UseInMemory();
            msg.EnableOutbox(o =>
            {
                o.PollingIntervalSeconds = 10;
                o.BatchSize = 50;
                o.MaxRetries = 3;
            });
        });
        var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
        options.OutboxEnabled.Should().BeTrue();
        options.PollingIntervalSeconds.Should().Be(10);
        options.BatchSize.Should().Be(50);
        options.MaxRetries.Should().Be(3);
        sp.Dispose();
    }

    [Fact]
    public void AddPragmaticMessaging_DefaultOptions_ShouldHaveCorrectDefaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
        options.OutboxEnabled.Should().BeFalse();
        options.PollingIntervalSeconds.Should().Be(5);
        options.BatchSize.Should().Be(100);
        options.MaxRetries.Should().Be(5);
        options.UseInMemoryDeadLetter.Should().BeTrue();
        sp.Dispose();
    }

    [Fact]
    public void AddPragmaticMessaging_DisableInMemoryDeadLetter_ShouldNotRegister()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging(msg => msg.DisableInMemoryDeadLetter());
        var sp = services.BuildServiceProvider();

        sp.GetService<IDeadLetterStore>().Should().BeNull();
        sp.Dispose();
    }
}
