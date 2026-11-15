using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Testing.Hosting;

/// <summary>
///     Builds a worker host (a host with no HTTP: <c>PragmaticApp.RunWorkerAsync</c>, jobs, message handlers) from its entry point, as the
///     application builds it, with the test's overrides; exposes its <see cref="Services" />; starts and stops it.
/// </summary>
/// <typeparam name="TEntryPoint">
///     Any type of the host's assembly: its entry point is what runs. A public one (the host's <c>[Module]</c>) saves the
///     <c>public partial class Program</c> a top-level <c>Program</c> would need.
/// </typeparam>
/// <remarks>
///     <para>
///         The generic-host counterpart of <c>WebApplicationFactory</c>, by the same mechanism: the entry point runs on a thread of its own up to
///         the moment its host is built, and is stopped there (<see cref="HostAbortedException" />). The host is the application's own, with
///         everything the generated <c>RunWorkerAsync</c> registers; what the test changes goes through <see cref="ConfigureServices" />,
///         applied after the application's registrations, and <see cref="UseSetting" />, passed as command-line arguments.
///     </para>
///     <para>
///         ⚠️ Invoking the entry point is reflection (<see cref="Assembly.EntryPoint" />), and the one place this package uses it: a test
///         tool running the application it was pointed at, as <c>WebApplicationFactory</c> does. Nothing of it is in the application.
///     </para>
/// </remarks>
public class PragmaticWorkerFactory<TEntryPoint> : IAsyncDisposable
    where TEntryPoint : class
{
    private readonly List<Action<IServiceCollection>> _configureServices = [];
    private readonly List<string> _arguments = [];
    private IHost? _host;
    private bool _started;

    /// <summary>How long the entry point may take to build its host before the factory gives up. One minute by default.</summary>
    public TimeSpan BuildTimeout { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The host's services, building the host first if it has not been built.</summary>
    public IServiceProvider Services => Host.Services;

    /// <summary>The host, built from the entry point on first use. Not started: <see cref="StartAsync" /> runs its hosted services.</summary>
    public IHost Host => _host ??= Build();

    /// <summary>Overrides the host's services, after the application registered its own: a replacement here wins.</summary>
    public PragmaticWorkerFactory<TEntryPoint> ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotBuilt();
        _configureServices.Add(configure);
        return this;
    }

    /// <summary>A configuration value, given to the entry point as the command-line argument <c>--key=value</c>.</summary>
    public PragmaticWorkerFactory<TEntryPoint> UseSetting(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        EnsureNotBuilt();
        _arguments.Add($"--{key}={value}");
        return this;
    }

    /// <summary>The host's environment (<see cref="HostDefaults.EnvironmentKey" />).</summary>
    public PragmaticWorkerFactory<TEntryPoint> UseEnvironment(string environment)
        => UseSetting(HostDefaults.EnvironmentKey, environment);

    /// <summary>Starts the host: its hosted services (the job processor, the message consumers) begin to run.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await Host.StartAsync(ct).ConfigureAwait(false);
        _started = true;
    }

    /// <summary>Stops the host's hosted services. The host stays built, and its services readable.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_host is null || !_started)
            return;

        _started = false;
        await _host.StopAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (_host is IAsyncDisposable asyncHost)
            await asyncHost.DisposeAsync().ConfigureAwait(false);
        else
            _host?.Dispose();

        _host = null;
        GC.SuppressFinalize(this);
    }

    private IHost Build()
    {
        var entryPoint = typeof(TEntryPoint).Assembly.EntryPoint
            ?? throw new InvalidOperationException(
                $"{typeof(TEntryPoint).Assembly.GetName().Name} has no entry point: PragmaticWorkerFactory runs a host's Main, so TEntryPoint has to be a type of the host's executable.");
        var configure = _configureServices.ToArray();
        var interception = new HostInterception(services =>
        {
            foreach (var apply in configure)
                apply(services);
        });
        object?[] parameters = entryPoint.GetParameters().Length == 0 ? [] : [_arguments.ToArray()];

        var thread = new Thread(() => RunEntryPoint(entryPoint, parameters, interception))
        {
            IsBackground = true,
            Name = $"{typeof(TEntryPoint).Assembly.GetName().Name} entry point",
        };
        thread.Start();

        try
        {
            if (!interception.Host.Wait(BuildTimeout))
                throw new TimeoutException($"The entry point of {typeof(TEntryPoint).Assembly.GetName().Name} built no host within {BuildTimeout}.");
        }
        catch (AggregateException failed) when (failed.InnerExceptions.Count == 1)
        {
            throw new InvalidOperationException(
                $"The entry point of {typeof(TEntryPoint).Assembly.GetName().Name} failed before it built its host.", failed.InnerExceptions[0]);
        }

        return interception.Host.Result;
    }

    private static void RunEntryPoint(MethodInfo entryPoint, object?[] parameters, HostInterception interception)
    {
        using var subscription = interception.Activate();
        try
        {
            var returned = entryPoint.Invoke(null, parameters);
            if (returned is Task running)
                running.GetAwaiter().GetResult();

            interception.Fail(new InvalidOperationException("The entry point returned without building a host."));
        }
        catch (Exception thrown)
        {
            // A synchronous Main's exception arrives wrapped by the invocation; an async one's does not.
            var failure = thrown is TargetInvocationException { InnerException: { } inner } ? inner : thrown;

            // HostAbortedException is the interception's own stop, once the host was built: the Fail below then changes nothing, since the
            // host is already set. Any other exception is the entry point failing before it built one.
            interception.Fail(failure);
        }
        finally
        {
            interception.Release();
        }
    }

    private void EnsureNotBuilt()
    {
        if (_host is not null)
            throw new InvalidOperationException("The host is built already: configure the factory before reading Services or starting it.");
    }
}
