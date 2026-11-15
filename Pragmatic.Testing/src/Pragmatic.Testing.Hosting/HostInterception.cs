using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Testing.Hosting;

/// <summary>
///     Listens to the generic host's diagnostic source while one entry point runs: applies the test's service overrides when the host is being
///     built (<c>HostBuilding</c>), and takes the host once it is built (<c>HostBuilt</c>), stopping the entry point there with
///     <see cref="HostAbortedException" /> so it never runs the host itself.
/// </summary>
/// <remarks>
///     <para>
///         The mechanism <c>WebApplicationFactory</c> uses (Microsoft.Extensions.HostFactoryResolver), for a host with no HTTP. The events are
///         public: <c>Microsoft.Extensions.Hosting</c> writes them from <c>HostBuilder.Build</c> and <c>HostApplicationBuilder.Build</c>.
///     </para>
///     <para>
///         Only the entry point this interception started is listened to: <see cref="Activate" /> marks its thread, and an event raised on any
///         other — a second factory in a parallel test, building its own host — is ignored.
///     </para>
/// </remarks>
internal sealed class HostInterception(Action<IServiceCollection> configureServices)
    : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private const string HostingSource = "Microsoft.Extensions.Hosting";

    private static readonly AsyncLocal<HostInterception?> Current = new();

    private readonly TaskCompletionSource<IHost> _host = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Lock _lock = new();

    /// <summary>The host the entry point built, or the failure that stopped it before.</summary>
    public Task<IHost> Host => _host.Task;

    /// <summary>Marks the calling thread as the entry point's and subscribes to the diagnostic sources from it.</summary>
    public IDisposable Activate()
    {
        Current.Value = this;
        return DiagnosticListener.AllListeners.Subscribe(this);
    }

    /// <summary>The entry point returned, or threw, without building a host.</summary>
    public void Fail(Exception failure) => _host.TrySetException(failure);

    /// <summary>Drops the subscriptions to the hosting sources.</summary>
    public void Release()
    {
        lock (_lock)
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();
        }
    }

    /// <inheritdoc />
    public void OnNext(DiagnosticListener value)
    {
        if (!ReferenceEquals(Current.Value, this) || value.Name != HostingSource)
            return;

        lock (_lock)
            _subscriptions.Add(value.Subscribe(this));
    }

    /// <inheritdoc />
    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (!ReferenceEquals(Current.Value, this) || _host.Task.IsCompleted)
            return;

        switch (value.Key)
        {
            // HostBuilder writes itself, and HostApplicationBuilder (Host.CreateApplicationBuilder, what RunWorkerAsync builds) writes its
            // IHostBuilder adapter, whose callbacks it applies after the entry point's own registrations: an override replaces them.
            case "HostBuilding" when value.Value is IHostBuilder builder:
                builder.ConfigureServices((_, services) => configureServices(services));
                break;
            case "HostBuilt" when value.Value is IHost host:
                _host.TrySetResult(host);
                // Unwinds the entry point out of Build, before it can run the host: the factory starts and stops it.
                throw new HostAbortedException();
        }
    }

    /// <inheritdoc />
    public void OnError(Exception error)
    {
    }

    /// <inheritdoc />
    public void OnCompleted()
    {
    }
}
