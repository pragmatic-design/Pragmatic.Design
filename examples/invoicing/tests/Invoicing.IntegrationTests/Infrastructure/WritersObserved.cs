using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Endpoints.Diagnostics;

namespace Invoicing.IntegrationTests.Infrastructure;

/// <summary>
///     The writers one host's JSON responses were written by while it is alive, read from the
///     <c>pragmatic.endpoints.json_responses</c> counter: what the host did, not what a generated file says.
/// </summary>
/// <remarks>
///     The counter's <see cref="Meter" /> is static, so a listener hears every response written in the test process,
///     and the assembly runs more than one host at once: the generated contract tests have a collection and a host of
///     their own, run beside this suite's. A measurement is kept only when the request being answered is one of
///     <c>host</c>'s, told by a singleton of the answering request's services being the host's own.
/// </remarks>
internal sealed class WritersObserved : IDisposable
{
    /// <summary>
    ///     The instrument is matched by name: a listener started before anything touched
    ///     <see cref="EndpointResponseMetrics" /> is told of it from inside that type's initializer, where
    ///     <see cref="EndpointResponseMetrics.JsonResponses" /> is still null, and a reference comparison never matches.
    /// </summary>
    private const string JsonResponsesName = "pragmatic.endpoints.json_responses";

    private readonly MeterListener _listener = new();
    private readonly List<string> _seen = [];
    private readonly IHttpContextAccessor _requests;
    private readonly IHostApplicationLifetime _host;

    /// <param name="host">The services of the host whose responses are counted.</param>
    public WritersObserved(IServiceProvider host)
    {
        _requests = host.GetRequiredService<IHttpContextAccessor>();
        _host = host.GetRequiredService<IHostApplicationLifetime>();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == EndpointResponseMetrics.MeterName && instrument.Name == JsonResponsesName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (!IsTheHosts())
                return;

            foreach (var tag in tags)
                if (tag.Key == EndpointResponseMetrics.WriterTag)
                    lock (_seen) _seen.Add((string)tag.Value!);
        });
        _listener.Start();
    }

    /// <summary><c>generated</c> or <c>serializer</c>, once per response of the host.</summary>
    public IReadOnlyList<string> Seen
    {
        get
        {
            lock (_seen) return [.. _seen];
        }
    }

    public void Dispose() => _listener.Dispose();

    /// <summary>Whether the request being answered on this flow, if any, is one of the host's.</summary>
    /// <remarks>
    ///     The counter is incremented inside the request, so the current request is the one the measurement is about.
    /// </remarks>
    private bool IsTheHosts()
        => _requests.HttpContext?.RequestServices.GetService<IHostApplicationLifetime>() is { } answering
           && ReferenceEquals(answering, _host);
}
