using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Email.Builder;
using Pragmatic.Email.Extensions;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     Demonstrates <c>EmailOptions.DefaultFrom</c> and CC/BCC addressing. The builder
///     requires a From address at <c>Build()</c> time, so <c>DefaultFrom</c> is the
///     application-wide sender you configure once via <c>AddPragmaticEmail(b =&gt; b.Configure(...))</c>
///     and reuse on every message, while CC and BCC recipients are set per message.
/// </summary>
public static class DefaultFromSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- DefaultFrom + CC/BCC ---");

        var services = new ServiceCollection();
        services.AddLogging();

        // Configure a process-wide default sender.
        var defaultFrom = EmailAddress.Validated("system@example.com", "System");
        services.AddPragmaticEmail(email =>
        {
            email.UseNullTransport();
            email.Configure(o => o.DefaultFrom = defaultFrom);
        });

        var harness = services.AddEmailTestHarness();
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IEmailSender>();

        // Reuse the configured default sender; add Cc and Bcc on this message.
        var message = new EmailMessageBuilder()
            .From(defaultFrom.Address, defaultFrom.DisplayName)
            .To("primary@example.com", "Primary")
            .Cc("manager@example.com")
            .Bcc("audit@example.com")
            .Subject("Quarterly summary")
            .TextBody("Body with the application default sender.")
            .Build();

        await sender.SendAsync(message);

        var sent = harness.Sent[0].Message;
        Console.WriteLine($"  From : {sent.From.DisplayName} <{sent.From.Address}>");
        Console.WriteLine($"  To   : {sent.To.Count} ({sent.To[0].Address})");
        Console.WriteLine($"  Cc   : {sent.Cc.Count} ({sent.Cc[0].Address})");
        Console.WriteLine($"  Bcc  : {sent.Bcc.Count} ({sent.Bcc[0].Address})");
        Console.WriteLine();
    }
}
