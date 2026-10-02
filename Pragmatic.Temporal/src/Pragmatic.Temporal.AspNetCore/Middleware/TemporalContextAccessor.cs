using Microsoft.AspNetCore.Http;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Context;

namespace Pragmatic.Temporal.AspNetCore.Middleware;

/// <summary>
///     Factory for retrieving <see cref="TemporalContext" /> from the current request.
/// </summary>
public sealed class TemporalContextAccessor
{
    private readonly IClock _clock;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TemporalOptions _options;

    /// <summary>Creates a new instance.</summary>
    public TemporalContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        TemporalOptions options,
        IClock clock)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options;
        _clock = clock;
    }

    /// <summary>Gets the current temporal context.</summary>
    public TemporalContext Context
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Items.TryGetValue(typeof(TemporalContext), out var ctx) == true
                && ctx is TemporalContext context)
                return context;

            // Return a default context if not in HTTP context — same policies the
            // middleware would apply, so background work behaves like request work.
            return new TemporalContext
            {
                Clock = _clock,
                ClientTimeZone = _options.DefaultTimeZone,
                BusinessTimeZone = _options.BusinessTimeZone,
                NonExistentTimeHandling = _options.NonExistentTimeHandling,
                AmbiguousTimeHandling = _options.AmbiguousTimeHandling,
                FirstDayOfWeek = _options.FirstDayOfWeek,
                DefaultCountryCode = _options.DefaultCountryCode
            };
        }
    }
}
