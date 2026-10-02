using Pragmatic.Identity.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Identity Samples                       ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// 1. ICurrentUser: built-in singletons, PrincipalKind, property composition
CurrentUserSample.Run();

// 2. Integration: DI injection, claim mapping, ASP.NET Core bridge
IntegrationPatternSample.Run();

// 3. Claims access, extension methods, runtime user inspection
ClaimsAndExtensionsSample.Run();

// 4. L2 — Groups/Roles/Permissions chain + resource-level authorization
GroupsAndRolesSample.Run();

// 5. Identity.Local — Register → Login → ChangePassword → password reset (real actions)
LocalIdentityFlowSample.Run();

// 6. Identity.Local.Jwt — token generation, claims, signing-key guard, rate limiting
JwtTokenSample.Run();

// 7. Identity.AspNetCore — ClaimsPrincipal → ICurrentUser bridge + claim mapping
AspNetCoreClaimsSample.Run();

// 8. Identity.Persistence — temporal role/group stores (EF in-memory)
PersistenceStoresSample.Run();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
