using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Aot.WebEndpoint;

/// <summary>How the request was read back, so the caller can check every binding at once.</summary>
public sealed class EchoDto
{
    public required string Id { get; init; }
    public int Page { get; init; }
    public required string Shade { get; init; }
    public string? Trace { get; init; }
}

/// <summary>The shades a caller may ask for.</summary>
public enum Shade
{
    /// <summary>The default.</summary>
    Plain = 0,

    /// <summary>Anything but.</summary>
    Bold = 1,
}

/// <summary>
///     Every binding source a generated endpoint has to read for itself: a route <c>Guid</c>, an
///     optional query <c>int</c> with a default, a query enum, and an optional header.
/// </summary>
/// <remarks>
///     A smoke that covers a JSON body and nothing else rests the claim on one shape. These are the
///     ones ASP.NET binds through <c>RequestDelegateFactory</c> — the path that does not survive an
///     AOT publish — and each has its own conversion: parse-or-400 for the
///     required ones, absent-means-default for the optional ones, and the difference between the two
///     is exactly what a hand-written binder gets wrong.
/// </remarks>
[DomainAction]
[Endpoint(HttpVerb.Get, "api/echo/{id}")]
public partial class EchoAction : DomainAction<EchoDto>
{
    /// <summary>From the route.</summary>
    public Guid Id { get; init; }

    // `set`, not `init`, on the optional ones: the generated handler assigns them after constructing
    // the action, because "absent" has to leave the declared default alone and an object initializer
    // cannot express that. A pre-existing constraint of the binding design, not of AOT.
    /// <summary>From the query string, with a default when the caller omits it.</summary>
    [FromQuery]
    public int Page { get; set; } = 7;

    /// <summary>From the query string, by name.</summary>
    [FromQuery]
    public Shade Shade { get; set; }

    /// <summary>From a header that may not be there.</summary>
    [FromHeader(Name = "X-Trace")]
    public string? Trace { get; set; }

    public override Task<Result<EchoDto, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<EchoDto, IError>>(new EchoDto
        {
            Id = Id.ToString(),
            Page = Page,
            Shade = Shade.ToString(),
            Trace = Trace,
        });
}
