using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Email;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Email;
using Pragmatic.Notifications.Extensions;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Notifications.Tests.Unit;

/// <summary>
///     A notification e-mail leaves from its tenant's address when an <see cref="ITenantEmailSenderResolver" />
///     names one (#742), and from the options' address otherwise.
/// </summary>
/// <remarks>
///     Through the container <c>AddSmtp</c> builds, not by constructing the channel: the resolver reaches the
///     channel as an optional constructor parameter, and that the container fills it is the half a hand-built
///     channel would not prove.
/// </remarks>
public sealed class EachTenantSendsFromItsOwnAddressTests
{
    [Fact]
    public async Task TwoTenants_SendFromTheirOwnAddresses()
    {
        var (channel, sent) = Build(resolver: new Senders());

        await channel.DeliverAsync(Recipient("acme"), Content());
        await channel.DeliverAsync(Recipient("globex"), Content());

        sent.Select(m => m.From.Address).Should().Equal("billing@acme.example", "billing@globex.example");
        sent[0].From.DisplayName.Should().Be("Acme");
    }

    /// <summary>A tenant the resolver does not know, and a recipient with no tenant, keep the options' address.</summary>
    [Fact]
    public async Task ATenantTheResolverDoesNotKnow_AndNoTenant_KeepTheOptionsAddress()
    {
        var (channel, sent) = Build(resolver: new Senders());

        await channel.DeliverAsync(Recipient("initech"), Content());
        await channel.DeliverAsync(Recipient(null), Content());

        sent.Select(m => m.From.Address).Should().Equal("noreply@example.com", "noreply@example.com");
    }

    /// <summary>The control: without a resolver, every tenant sends from the options' address, as before.</summary>
    [Fact]
    public async Task WithoutAResolver_EveryTenantSendsFromTheOptionsAddress()
    {
        var (channel, sent) = Build(resolver: null);

        await channel.DeliverAsync(Recipient("acme"), Content());

        sent.Single().From.Address.Should().Be("noreply@example.com");
    }

    private static (INotificationChannel Channel, List<EmailMessage> Sent) Build(ITenantEmailSenderResolver? resolver)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddPragmaticNotifications(n => n.AddSmtp(
            sender => sender.SenderAddress = "noreply@example.com",
            smtp => smtp.Host = "localhost"));

        var capture = new CapturingSender();
        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender>(capture);
        if (resolver is not null)
            services.AddSingleton(resolver);

        var provider = services.BuildServiceProvider();
        var channel = provider.GetRequiredService<INotificationChannelFactory>().GetChannel(NotificationChannel.Email)!;
        return (channel, capture.Sent);
    }

    private static ResolvedRecipient Recipient(string? tenant)
        => new("someone@example.com", NotificationChannel.Email, null, tenant, null);

    private static NotificationContent Content() => new() { Subject = "Your invoice", Body = "Attached." };

    private sealed class Senders : ITenantEmailSenderResolver
    {
        public ValueTask<EmailSenderIdentity?> ResolveAsync(string tenantId, CancellationToken ct = default)
            => ValueTask.FromResult(tenantId switch
            {
                "acme" => new EmailSenderIdentity("billing@acme.example", "Acme"),
                "globex" => new EmailSenderIdentity("billing@globex.example"),
                _ => null
            });
    }

    private sealed class CapturingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(EmailResult.Succeeded("id"));
        }
    }
}
