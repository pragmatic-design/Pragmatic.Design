using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Email.Transport;

namespace Pragmatic.Email.Testing;

/// <summary>
///     DI registration for email test harness.
/// </summary>
public static class EmailTestExtensions
{
    /// <summary>
    ///     Replaces <see cref="IEmailTransport"/> with <see cref="InMemoryTransport"/>.
    ///     Returns the transport instance for assertions.
    /// </summary>
    public static InMemoryTransport AddEmailTestHarness(this IServiceCollection services)
    {
        var transport = new InMemoryTransport();
        services.RemoveAll<IEmailTransport>();
        services.AddSingleton<IEmailTransport>(transport);
        services.AddSingleton(transport);
        return transport;
    }
}
