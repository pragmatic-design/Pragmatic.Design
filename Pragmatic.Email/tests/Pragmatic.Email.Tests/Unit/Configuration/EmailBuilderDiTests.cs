using System.Security.Cryptography;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Extensions;
using Pragmatic.Email.Middleware;
using Pragmatic.Email.Security;
using Pragmatic.Email.Transport;

namespace Pragmatic.Email.Tests.Unit.Configuration;

public sealed class EmailBuilderDiTests
{
    [Fact]
    public void UseNullTransport_RegistersNullTransport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticEmail(b => b.UseNullTransport());

        var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IEmailTransport>().Name.Should().Be("Null");
    }

    [Fact]
    public void UseTransport_MultipleTimes_RegistersOnlyLast()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticEmail(b =>
        {
            b.UseNullTransport();
            b.UseTransport<CustomTransport>();
        });

        var sp = services.BuildServiceProvider();

        // Only one IEmailTransport must be registered, and it must be the last one.
        sp.GetServices<IEmailTransport>().Should().ContainSingle();
        sp.GetRequiredService<IEmailTransport>().Should().BeOfType<CustomTransport>();
    }

    [Fact]
    public void AddMiddleware_AccumulatesMiddleware()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticEmail(b =>
        {
            b.AddMiddleware<NoopMiddleware>();
            b.AddMiddleware<OtherNoopMiddleware>();
        });

        var sp = services.BuildServiceProvider();

        // Asserted by type rather than by count: the pipeline also carries always-on middleware
        // (DefaultFromMiddleware), so a bare count breaks whenever one is added.
        var middleware = sp.GetServices<IEmailMiddleware>().ToList();
        middleware.Should().ContainSingle(m => m is NoopMiddleware);
        middleware.Should().ContainSingle(m => m is OtherNoopMiddleware);
    }

    [Fact]
    public void EnableDkim_RegistersDkimMiddleware()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticEmail(b => b.EnableDkim(o =>
        {
            o.Domain = "example.com";
            o.Selector = "test";
            o.PrivateKeyPem = pem;
        }));

        var sp = services.BuildServiceProvider();

        sp.GetServices<IEmailMiddleware>().Should().ContainSingle(m => m is DkimMiddleware);
        sp.GetRequiredService<IOptions<DkimOptions>>().Value.Domain.Should().Be("example.com");
    }

    [Fact]
    public void Configure_SetsDefaultFrom()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticEmail(b => b.Configure(o =>
            o.DefaultFrom = new EmailAddress("noreply@example.com", "No Reply")));

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<EmailOptions>>().Value;

        options.DefaultFrom.Should().NotBeNull();
        options.DefaultFrom!.Value.Address.Should().Be("noreply@example.com");
    }

    [Fact]
    public void EmailOptions_DefaultFrom_DefaultsToNull()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticEmail();

        var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IOptions<EmailOptions>>().Value.DefaultFrom.Should().BeNull();
    }

    private sealed class CustomTransport : IEmailTransport
    {
        public string Name => "Custom";
        public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
            => Task.FromResult(EmailResult.Succeeded(message.MessageId));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopMiddleware : IEmailMiddleware
    {
        public int Order => 0;
        public Task<EmailMessage> ProcessAsync(EmailMessage message, Func<EmailMessage, Task<EmailMessage>> next, CancellationToken ct)
            => next(message);
    }

    private sealed class OtherNoopMiddleware : IEmailMiddleware
    {
        public int Order => 1;
        public Task<EmailMessage> ProcessAsync(EmailMessage message, Func<EmailMessage, Task<EmailMessage>> next, CancellationToken ct)
            => next(message);
    }
}
