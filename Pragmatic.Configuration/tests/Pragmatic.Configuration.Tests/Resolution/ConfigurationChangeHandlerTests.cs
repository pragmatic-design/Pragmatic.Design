using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Configuration.Extensions;
using Pragmatic.Configuration.Providers;
using Xunit;

namespace Pragmatic.Configuration.Tests.Resolution;

/// <summary>
///     Verifies the typed <see cref="IConfigurationChangeHandler{TOptions}" /> path: a change to a key under
///     an option's section is dispatched to that option's handler, and unrelated keys are ignored.
/// </summary>
public class ConfigurationChangeHandlerTests
{
    private sealed class BookingOptions;   // section "Booking"

    private sealed class Recorder
    {
        public TaskCompletionSource<ConfigurationChange> Received { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BookingHandler(Recorder recorder) : IConfigurationChangeHandler<BookingOptions>
    {
        public Task OnChangedAsync(ConfigurationChange change, CancellationToken ct = default)
        {
            recorder.Received.TrySetResult(change);
            return Task.CompletedTask;
        }
    }

    private static (ServiceProvider Sp, Recorder Recorder) Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfigurationStore, InMemoryConfigurationStore>();
        services.AddSingleton<Recorder>();
        services.AddConfigurationChangeHandler<BookingOptions, BookingHandler>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<Recorder>());
    }

    [Fact]
    public async Task ChangeUnderOptionSection_InvokesTypedHandler()
    {
        var (sp, recorder) = Build();
        await using (sp.ConfigureAwait(false))
        {
            var store = sp.GetRequiredService<IConfigurationStore>();
            var dispatchers = sp.GetServices<IHostedService>().ToArray();

            using var cts = new CancellationTokenSource();
            foreach (var d in dispatchers)
                await d.StartAsync(cts.Token);

            await Task.Delay(200); // let the watch loop subscribe before we write

            await store.SetAsync("Booking:CancellationWindowHours", "48");

            var completed = await Task.WhenAny(recorder.Received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            completed.Should().Be(recorder.Received.Task, "the handler must fire for a key under its section");
            var change = await recorder.Received.Task;
            change.Key.Should().Be("Booking:CancellationWindowHours");
            change.NewValue.Should().Be("48");

            foreach (var d in dispatchers)
                await d.StopAsync(cts.Token);
        }
    }

    [Fact]
    public async Task ChangeOutsideOptionSection_DoesNotInvokeHandler()
    {
        var (sp, recorder) = Build();
        await using (sp.ConfigureAwait(false))
        {
            var store = sp.GetRequiredService<IConfigurationStore>();
            var dispatchers = sp.GetServices<IHostedService>().ToArray();

            using var cts = new CancellationTokenSource();
            foreach (var d in dispatchers)
                await d.StartAsync(cts.Token);

            await Task.Delay(200);

            await store.SetAsync("Payment:ApiKey", "x");

            var completed = await Task.WhenAny(recorder.Received.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            completed.Should().NotBe(recorder.Received.Task, "a key outside the option's section must be ignored");

            foreach (var d in dispatchers)
                await d.StopAsync(cts.Token);
        }
    }
}
