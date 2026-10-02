using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Tracking;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Notifications.Tests.Unit;

/// <summary>
///     A notification written in the language of whoever receives it.
/// </summary>
/// <remarks>
///     <para>
///         <c>ResolvedRecipient</c> has carried a <c>Locale</c> since it was written, the resolver
///         fills it from the recipient's preferences, and nothing in the module could use it: the
///         pipeline resolves recipients and then hands each one the same <c>request.Content</c>,
///         already rendered before any recipient existed. Two people in one workspace with two
///         languages received the same sentences.
///     </para>
///     <para>
///         ⚠️ It was never a job-only problem. Every caller of <c>INotificationService</c> has the
///         same shape, because <c>NotificationRequest</c> requires a <c>Content</c> — so a request
///         addressed to a role, a tenant or a list of users is one rendered message for all of them.
///     </para>
///     <para>
///         ⚠️ And the way out is not to render N times in the caller. A caller that loops over
///         recipients has to resolve recipients, which is the responsibility the pipeline exists to
///         hold — it would have to re-implement preferences, opt-outs and channel routing to know
///         who it is rendering for.
///     </para>
/// </remarks>
public sealed class ContentPerRecipientTests
{
    private readonly RecipientResolverMock _resolver = new();
    private readonly NotificationRouterMock _router = new();
    private readonly NotificationChannelFactoryMock _channelFactory = new();
    private readonly INotificationStore _store = new InMemoryNotificationStore();
    private readonly NotificationPipeline _pipeline;

    public ContentPerRecipientTests()
        => _pipeline = new NotificationPipeline(
            _resolver, _router, _channelFactory, _store, NullLogger<NotificationPipeline>.Instance);

    private static ResolvedRecipient At(string address, string? locale)
        => new(address, NotificationChannel.Email, locale, null, null);

    /// <summary>Records what each delivery was handed, so the assertions read the content and not a count.</summary>
    private List<(string Address, NotificationContent Content)> CaptureDeliveries()
    {
        var seen = new List<(string, NotificationContent)>();

        var channel = new NotificationChannelMock();
        channel.Channel.Returns(NotificationChannel.Email);
        channel.DeliverAsync.Returns((ResolvedRecipient recipient, NotificationContent content, CancellationToken _) =>
        {
            seen.Add((recipient.Address, content));
            return Task.FromResult(DeliveryResult.Succeeded());
        });

        _channelFactory.GetChannel.When(NotificationChannel.Email, Arg.Any<string?>()).Returns(channel);
        _router.Route.Returns(NotificationChannel.Email);

        return seen;
    }

    /// <summary>
    ///     Two recipients of one notification, two locales, two messages.
    /// </summary>
    /// <remarks>
    ///     The pair is the assertion. One recipient alone would be satisfied by a pipeline that calls
    ///     the factory once and reuses the answer, which is the defect wearing a different hat.
    /// </remarks>
    [Fact]
    public async Task TwoRecipientsWithDifferentLocales_AreDeliveredDifferentContent()
    {
        var deliveries = CaptureDeliveries();

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>(
            [At("ada@example.com", "it-IT"), At("bob@example.com", "en-US")]));

        var request = new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Direct("ignored-by-the-mock@example.com"),
            Content = new NotificationContent { Subject = "Fallback", Body = "Fallback body" },
            ContentFor = (recipient, _) => new ValueTask<NotificationContent>(
                new NotificationContent
                {
                    Subject = $"Subject in {recipient.Locale}",
                    Body = $"Body in {recipient.Locale}",
                }),
        };

        var result = await _pipeline.ExecuteAsync(request);

        result.Success.Should().BeTrue();

        deliveries.Should().Contain(d => d.Address == "ada@example.com" && d.Content.Subject == "Subject in it-IT",
            "the message is produced for the recipient, whose locale the resolver had already found");
        deliveries.Should().Contain(d => d.Address == "bob@example.com" && d.Content.Subject == "Subject in en-US",
            "and the other recipient of the same notification gets the other language");
    }

    /// <summary>
    ///     ⚠️ The first control: a request carrying only <c>Content</c> delivers exactly what it did.
    /// </summary>
    /// <remarks>
    ///     Every caller in existence uses that form, and none of them will be edited. If the pipeline
    ///     ever required the new one, this is what would say so.
    /// </remarks>
    [Fact]
    public async Task ARequestWithOnlyContent_DeliversThatContentToEveryone()
    {
        var deliveries = CaptureDeliveries();

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>(
            [At("ada@example.com", "it-IT"), At("bob@example.com", "en-US")]));

        var request = new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Direct("ignored@example.com"),
            Content = new NotificationContent { Subject = "The one message", Body = "For everybody" },
        };

        (await _pipeline.ExecuteAsync(request)).Success.Should().BeTrue();

        deliveries.Should().HaveCount(2);
        deliveries.Should().OnlyContain(d => d.Content.Subject == "The one message",
            "the form every caller uses today is untouched, differing locales included");
    }

    /// <summary>
    ///     ⚠️ The second control: a recipient with no locale gets the default, not an empty message.
    /// </summary>
    /// <remarks>
    ///     A null <c>Locale</c> is the ordinary case — a membership imported from another tracker
    ///     arrives without a preference — so falling through to nothing would be the same defect one
    ///     layer down. What the factory does with a null locale is the caller's business; what the
    ///     pipeline must not do is skip the delivery.
    /// </remarks>
    [Fact]
    public async Task ARecipientWithNoLocale_IsStillDelivered()
    {
        var deliveries = CaptureDeliveries();

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>(
            [At("ada@example.com", "it-IT"), At("nobody@example.com", null)]));

        var request = new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Direct("ignored@example.com"),
            Content = new NotificationContent { Subject = "Fallback", Body = "Fallback body" },
            ContentFor = (recipient, _) => new ValueTask<NotificationContent>(
                recipient.Locale is null
                    ? new NotificationContent { Subject = "Default", Body = "Default body" }
                    : new NotificationContent
                    {
                        Subject = $"Subject in {recipient.Locale}",
                        Body = $"Body in {recipient.Locale}",
                    }),
        };

        (await _pipeline.ExecuteAsync(request)).Success.Should().BeTrue();

        deliveries.Should().HaveCount(2, "a missing preference is not a reason to send nothing");
        deliveries.Should().Contain(d => d.Address == "nobody@example.com" && d.Content.Subject == "Default");
        deliveries.Should().Contain(d => d.Address == "ada@example.com" && d.Content.Subject == "Subject in it-IT",
            "the control on the control: the other recipient still gets theirs");
    }

    /// <summary>
    ///     The tracking record carries the subject that was actually sent.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A decision rather than a detail, and the issue said so. One record already exists per
    ///     delivery, so the honest value is the subject that delivery carried — writing the fallback
    ///     would make the trail disagree with the mailbox for every recipient who got a translation.
    /// </remarks>
    [Fact]
    public async Task TheTrackingRecord_CarriesTheSubjectThatWasSent()
    {
        CaptureDeliveries();

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>(
            [At("ada@example.com", "it-IT")]));

        var request = new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Direct("ignored@example.com"),
            Content = new NotificationContent { Subject = "Fallback", Body = "Fallback body" },
            ContentFor = (recipient, _) => new ValueTask<NotificationContent>(
                new NotificationContent
                {
                    Subject = $"Subject in {recipient.Locale}",
                    Body = $"Body in {recipient.Locale}",
                }),
        };

        var result = await _pipeline.ExecuteAsync(request);

        result.Success.Should().BeTrue();
        Assert.NotNull(result.DeliveryIds);

        var record = await _store.GetByIdAsync(result.DeliveryIds[0]);

        Assert.NotNull(record);
        record.Subject.Should().Be("Subject in it-IT",
            "the trail says what was sent, not what would have been sent had nobody translated it");
    }
}
