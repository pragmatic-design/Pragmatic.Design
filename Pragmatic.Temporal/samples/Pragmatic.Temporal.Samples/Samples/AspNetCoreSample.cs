using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Pragmatic.Temporal.AspNetCore;
using Pragmatic.Temporal.AspNetCore.Detection;
using Pragmatic.Temporal.Timezone;

namespace Pragmatic.Temporal.Samples.Samples;

/// <summary>
///     Demonstrates the ASP.NET Core timezone detection strategies in isolation, driving each one
///     with a synthetic <see cref="DefaultHttpContext" /> (no web server required). These are the
///     same strategies the <c>TemporalContextMiddleware</c> runs, in priority order, to pick the
///     client timezone for each request.
///     <para>
///         Wiring in a real app (shown here as reference, not executed):
///         <code>
///         builder.Services.AddPragmaticTemporalAspNetCore(
///             temporal => temporal.BusinessTimeZone = TimeZoneResolver.GetTimeZone("Europe/Rome"),
///             asp      => asp.TimezoneHeader = "X-Timezone");
///         app.UsePragmaticTemporal(); // registers TemporalContextMiddleware
///         </code>
///     </para>
/// </summary>
public static class AspNetCoreSample
{
    public static void Run()
    {
        Console.WriteLine("--- ASP.NET Core Timezone Detection Sample ---\n");

        // ------------------------------------------------------------------
        // Header strategy: reads X-Timezone (configurable).
        // ------------------------------------------------------------------
        var header = new HeaderTimeZoneStrategy();
        var headerCtx = new DefaultHttpContext();
        headerCtx.Request.Headers["X-Timezone"] = "America/New_York";
        Console.WriteLine($"Header   (priority {header.Priority}): X-Timezone=America/New_York -> {Describe(header.Detect(headerCtx))}");

        // ------------------------------------------------------------------
        // Query-string strategy: reads ?tz=... (handy for debugging a single request).
        // ------------------------------------------------------------------
        var query = new QueryStringTimeZoneStrategy();
        var queryCtx = new DefaultHttpContext();
        queryCtx.Request.QueryString = new QueryString("?tz=Europe/Rome");
        Console.WriteLine($"Query    (priority {query.Priority}): ?tz=Europe/Rome -> {Describe(query.Detect(queryCtx))}");

        // ------------------------------------------------------------------
        // Claims strategy: reads a 'timezone' claim from the authenticated user.
        // ------------------------------------------------------------------
        var claims = new ClaimsTimeZoneStrategy();
        var claimsCtx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("timezone", "Asia/Tokyo")], authenticationType: "test"))
        };
        Console.WriteLine($"Claims   (priority {claims.Priority}): claim timezone=Asia/Tokyo -> {Describe(claims.Detect(claimsCtx))}");

        // ------------------------------------------------------------------
        // Cookie strategy: reads the 'tz' cookie (the default CookieName) from the Cookie header.
        // ------------------------------------------------------------------
        var cookie = new CookieTimeZoneStrategy();
        var cookieCtx = new DefaultHttpContext();
        cookieCtx.Request.Headers["Cookie"] = $"{cookie.CookieName}=Australia/Sydney";
        Console.WriteLine($"Cookie   (priority {cookie.Priority}): cookie {cookie.CookieName}=Australia/Sydney -> {Describe(cookie.Detect(cookieCtx))}");

        // ------------------------------------------------------------------
        // No signal -> strategy returns null, middleware then falls back to the default zone.
        // ------------------------------------------------------------------
        Console.WriteLine($"\nHeader with no X-Timezone present -> {Describe(header.Detect(new DefaultHttpContext()))}");

        // ------------------------------------------------------------------
        // Length cap: oversized values are rejected (basic input hardening).
        // ------------------------------------------------------------------
        var oversizedCtx = new DefaultHttpContext();
        oversizedCtx.Request.Headers["X-Timezone"] = new string('x', 200);
        Console.WriteLine($"Header with 200-char value (capped at 64) -> {Describe(header.Detect(oversizedCtx))}");

        // ------------------------------------------------------------------
        // Options object: default strategies, evaluated in ascending Priority.
        // ------------------------------------------------------------------
        var options = new TemporalAspNetCoreOptions();
        Console.WriteLine("\nDefault detection strategy order (by priority):");
        foreach (var strategy in options.DetectionStrategies.OrderBy(s => s.Priority))
            Console.WriteLine($"  {strategy.Priority,4}  {strategy.GetType().Name}");

        Console.WriteLine();
    }

    private static string Describe(TimeZoneInfo? zone)
        => zone is null ? "<none>" : TimeZoneResolver.GetIanaId(zone);
}
