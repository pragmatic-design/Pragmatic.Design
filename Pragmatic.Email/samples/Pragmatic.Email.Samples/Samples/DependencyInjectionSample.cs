using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Email;
using Pragmatic.Email.Builder;
using Pragmatic.Email.Extensions;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     Wires the email system through DI with <c>AddPragmaticEmail</c> + the
///     <c>EmailBuilder</c> callback, then resolves the public <see cref="IEmailSender"/>
///     and sends a message. This is the production-shaped path: the sender runs the
///     middleware pipeline and then hands off to the registered transport.
///     <c>AddEmailTestHarness</c> swaps in an <see cref="InMemoryTransport"/> so the
///     sample needs no SMTP server.
/// </summary>
public static class DependencyInjectionSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- DependencyInjection (AddPragmaticEmail + EmailBuilder) ---");

        var services = new ServiceCollection();
        services.AddLogging();

        // AddPragmaticEmail is the DI entry point; the EmailBuilder callback selects transport + middleware.
        services.AddPragmaticEmail(email => email.UseNullTransport());

        // Replace the configured transport with the recording harness for assertions.
        var harness = services.AddEmailTestHarness();

        await using var provider = services.BuildServiceProvider();

        // IEmailSender is the public send API — it owns the pipeline + transport handoff.
        var sender = provider.GetRequiredService<IEmailSender>();

        var message = new EmailMessageBuilder()
            .From("noreply@example.com", "Example")
            .To("user@example.com")
            .Subject("Wired through DI")
            .TextBody("Resolved IEmailSender from the container.")
            .Build();

        var result = await sender.SendAsync(message);

        Console.WriteLine($"  IEmailSender resolved : {sender.GetType().Name}");
        Console.WriteLine($"  send succeeded        : {result.Success}");
        Console.WriteLine($"  harness recorded      : {harness.Sent.Count} message(s)");
        Console.WriteLine();
    }
}
