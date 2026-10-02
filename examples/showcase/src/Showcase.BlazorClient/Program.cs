using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Showcase.BlazorClient;

/// <summary>
///     Consumer of the SG-generated typed client. In a real Blazor WASM app this would be
///     <c>WebAssemblyHostBuilder</c>; here it resolves <see cref="IBookingClient"/> from DI and reports the
///     operations it exposes.
///     <para>
///         Behavioral coverage lives in <c>Showcase.IntegrationTests.Endpoints.GeneratedClientTests</c>, which
///         drives this same client against the running app. That distinction matters: this entry point can only
///         show that the client <i>compiles and resolves</i>, and proving no more than that is how dropped query
///         parameters and a GET issued as a POST stayed invisible.
///     </para>
/// </summary>
/// <remarks>
///     Declared in a namespace on purpose: top-level statements emit a global <c>Program</c>, which collides
///     (CS0433) with <c>Showcase.Host</c>'s own when the integration tests reference both.
/// </remarks>
public static class Program
{
    public static void Main()
    {
        // Built through the host builder rather than ServiceCollection.BuildServiceProvider(): the latter
        // trips ASP0000 under the Web SDK's analyzers, and silencing an analyzer to keep a demo compiling
        // is the wrong trade — the host builder is also what a real Blazor WASM entry point uses.
        var builder = Host.CreateApplicationBuilder();

        // SG-generated from the Showcase.Booking manifest → IBookingClient + BookingHttpClient
        builder.Services.AddBookingClient("https://localhost:5001");

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<IBookingClient>();

        Console.WriteLine($"Pragmatic Client SG: resolved {client.GetType().Name} for IBookingClient.");
        Console.WriteLine($"Operations available: {typeof(IBookingClient).GetMethods().Length}.");
        Console.WriteLine("Generated from the domain manifest at compile time. Zero domain DLL dependencies.");
    }
}
