using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Email.Builder;
using Pragmatic.Email.Extensions;
using Pragmatic.Email.Middleware;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     Registers custom <see cref="IEmailMiddleware"/> via <c>EmailBuilder.AddMiddleware&lt;T&gt;</c>
///     and observes them transform the message as it flows through the pipeline driven by
///     <see cref="IEmailSender"/>. Two stages with different <see cref="IEmailMiddleware.Order"/>
///     values prove the ordering contract (lower runs first).
/// </summary>
public static class CustomMiddlewareSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- CustomMiddleware (AddMiddleware<T>) ---");

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPragmaticEmail(email => email
            .UseNullTransport()
            .AddMiddleware<SubjectTagMiddleware>()   // Order 10 — runs first
            .AddMiddleware<FooterMiddleware>());      // Order 20 — runs second

        var harness = services.AddEmailTestHarness();
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IEmailSender>();

        var message = new EmailMessageBuilder()
            .From("noreply@example.com")
            .To("user@example.com")
            .Subject("Release notes")
            .TextBody("Version 1.0 is out.")
            .Build();

        await sender.SendAsync(message);

        var sent = harness.Sent[0].Message;
        Console.WriteLine($"  Subject after pipeline : {sent.Subject}");
        Console.WriteLine($"  Body after pipeline    :\n    {sent.TextBody!.Replace("\n", "\n    ")}");
        Console.WriteLine();
    }

    /// <summary>Prefixes the subject with an environment tag.</summary>
    private sealed class SubjectTagMiddleware : IEmailMiddleware
    {
        public int Order => 10;

        public Task<EmailMessage> ProcessAsync(
            EmailMessage message,
            Func<EmailMessage, Task<EmailMessage>> next,
            CancellationToken ct)
            => next(message with { Subject = $"[STAGING] {message.Subject}" });
    }

    /// <summary>Appends a standard footer to the plain-text body.</summary>
    private sealed class FooterMiddleware : IEmailMiddleware
    {
        public int Order => 20;

        public Task<EmailMessage> ProcessAsync(
            EmailMessage message,
            Func<EmailMessage, Task<EmailMessage>> next,
            CancellationToken ct)
            => next(message with { TextBody = $"{message.TextBody}\n--\nSent by Pragmatic.Email" });
    }
}
