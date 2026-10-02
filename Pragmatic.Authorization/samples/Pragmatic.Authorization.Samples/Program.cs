using Pragmatic.Authorization.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Authorization Samples                  ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// 1. ResourcePolicy: composable rules with & | ! operators
ResourcePolicySample.Run();

// 2. WildcardMatcher: hierarchical permission patterns
WildcardMatcherSample.Run();

// 3. Roles, permissions, provider chain, ABAC
RolePermissionSample.Run();

// 4. Policy evaluation: runnable checks against real ICurrentUser
PolicyEvaluationSample.Run();

// 5. AsyncResourcePolicy: I/O-bound checks + async composition
await AsyncResourcePolicySample.RunAsync();

// 6. PolicySerializer: ResourcePolicy -> PolicyExpression -> JSON round-trip
PolicySerializationSample.Run();

// 7. IPermissionCacheInvalidator: per-user and per-tenant invalidation
await CacheInvalidatorSample.RunAsync();

// 8. Custom IPermissionProvider composed via CompositePermissionProvider
await CustomPermissionProviderSample.RunAsync();

// 9. Management package flow (CreateRole / AssignRoleToUser / GetEffectivePermissions)
await ManagementSample.RunAsync();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
