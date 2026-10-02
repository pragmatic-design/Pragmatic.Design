using Pragmatic.Actions.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Actions Samples                        ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ── Pattern Catalog ──────────────────────────────────────────────────────────

// 1. All action patterns with SG output description
ActionCatalogSample.Run();

// 2. Error handling: typed errors, implicit conversions, multi-error
ErrorPatternsSample.Run();

// ── Mutation & Pipeline ──────────────────────────────────────────────────────

// 3. Mutation: create, update, pipeline flow
MutationPatternSample.Run();

// 4. DI wiring: registration, invoker usage, sub-boundary interfaces
DiWiringSample.Run();

// ── Advanced ─────────────────────────────────────────────────────────────────

// 5. CompositeAction, LoadEntity, Query pattern
AdvancedPatternsSample.Run();

// ── Boundaries & Authorization ───────────────────────────────────────────────

// 6. Boundary definitions, UseLocal/UseRemote, one registration per boundary (real DI)
BoundarySample.Run();

// 7. BoundaryVisibility.Internal + [ReadAccess<T>] cross-boundary read access
BoundaryVisibilitySample.Run();

// 8. Permissions/policies, IResourceAuthorizer, internal-call bypass, custom filter
await AuthorizationSample.RunAsync();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();

// Note: Action definitions are in Actions/, Queries/, Errors/, Services/ folders.
// Build this project and inspect obj/GeneratedFiles/ for SG output.
