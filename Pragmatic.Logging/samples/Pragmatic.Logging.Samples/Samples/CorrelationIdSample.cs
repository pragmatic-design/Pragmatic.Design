using Microsoft.AspNetCore.Http;
using Pragmatic.Logging.AspNetCore;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     <see cref="CorrelationIdProvider"/> derives a stable correlation id for every request:
///     it reuses a safe inbound <c>X-Correlation-ID</c> header when present, otherwise generates
///     a fresh one — and always echoes it back on the response so clients can correlate.
///     Untrusted header values are validated (bounded length, safe charset) to prevent
///     header-injection / response-splitting. This sample drives it with an in-memory
///     <see cref="DefaultHttpContext"/>, so no web server is required.
/// </summary>
public static class CorrelationIdSample
{
    public static void Run()
    {
        Console.WriteLine("\n--- Correlation id propagation ---");

        // Case 1: no inbound header → a fresh id is minted and written to the response.
        var fresh = new DefaultHttpContext();
        var generated = CorrelationIdProvider.GetOrCreateCorrelationId(fresh);
        Console.WriteLine($"No inbound header     → generated:  {generated}");
        Console.WriteLine($"  echoed on response  → X-Correlation-ID: {fresh.Response.Headers["X-Correlation-ID"]}");

        // Case 2: a valid inbound header is trusted and propagated end-to-end.
        var inbound = new DefaultHttpContext();
        inbound.Request.Headers["X-Correlation-ID"] = "order-flow-42";
        var propagated = CorrelationIdProvider.GetOrCreateCorrelationId(inbound);
        Console.WriteLine($"\nValid inbound header  → propagated: {propagated}");
        Console.WriteLine($"  echoed on response  → X-Correlation-ID: {inbound.Response.Headers["X-Correlation-ID"]}");

        // Case 3: a malicious inbound header (CR/LF injection) is rejected — a safe id is used.
        var malicious = new DefaultHttpContext();
        malicious.Request.Headers["X-Correlation-ID"] = "evil\r\nSet-Cookie: pwned=1";
        var safe = CorrelationIdProvider.GetOrCreateCorrelationId(malicious);
        Console.WriteLine($"\nMalicious inbound header rejected → safe id: {safe}");
        Console.WriteLine($"  IsValidCorrelationId(\"evil\\r\\n...\") = {CorrelationIdProvider.IsValidCorrelationId("evil\r\nSet-Cookie: pwned=1")}");
        Console.WriteLine($"  IsValidCorrelationId(\"order-flow-42\") = {CorrelationIdProvider.IsValidCorrelationId("order-flow-42")}");
    }
}
