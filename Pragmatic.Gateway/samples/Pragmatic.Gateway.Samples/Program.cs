using Pragmatic.Gateway.Samples;

// Pragmatic.Gateway runnable samples.
//
// The Gateway is a YARP-based reverse proxy. These samples deliberately do NOT stand up a live
// proxy host: they build the public configuration model (routes, clusters, auth, CORS, TLS,
// resilience), exercise the public resilience helpers against an in-memory circuit-breaker store,
// and run the maintenance / tenant response logic against a DefaultHttpContext.
//
// Internal middleware (MaintenanceMiddleware, TenantRoutingMiddleware, AgentRouteProvider) is not
// visible to this assembly; those samples faithfully reproduce the middleware's observable logic
// using only the public surface, with comments pointing back to the production type.

Console.WriteLine("Pragmatic.Gateway — Samples");

GatewayOptionsSample.Run();
AuthenticationSample.Run();
RateLimitCorsTlsSample.Run();
DynamicRoutingSample.Run();
await ResilienceSample.RunAsync();
await MaintenanceSample.RunAsync();
TenantRoutingSample.Run();
OperationalSample.Run();

Console.WriteLine();
Console.WriteLine("All Gateway samples completed.");
