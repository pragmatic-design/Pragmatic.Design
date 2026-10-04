# Examples: pragmatic-use-identity

Copied from `examples`, which compiles in the repository and is exercised by
`examples/time-off/tests/TimeOff.IntegrationTests` and `examples/invoicing/tests/Invoicing.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`time-off/src/TimeOff.Leave/LeaveModule.cs`](time-off/src/TimeOff.Leave/LeaveModule.cs) | Local accounts: `[UsePackage<LocalIdentityPackage, …>]`, exposing only the identity actions the application wants, anonymous where they must be |
| [`time-off/src/TimeOff.Leave/Employees/Employee.cs`](time-off/src/TimeOff.Leave/Employees/Employee.cs) | The user entity: `[PragmaticUser]` owning its `LocalIdentity`, and sessions revoked by rotating the security stamp |
| [`time-off/src/TimeOff.Leave/Infrastructure/Identity/EmployeeClaims.cs`](time-off/src/TimeOff.Leave/Infrastructure/Identity/EmployeeClaims.cs) | `IUserClaimsContributor`: what the token says, that is a pseudonymous subject, the display name, the roles |
| [`time-off/src/TimeOff.Host/Program.cs`](time-off/src/TimeOff.Host/Program.cs) | `UseJwtAuthentication()`, the claims contributor, `AddIdentitySecurityAuditing<TLocator>()`, the out-of-band password-reset notifier |
| [`time-off/src/TimeOff.Host/Identity/TheEmployeeASignInWasAbout.cs`](time-off/src/TimeOff.Host/Identity/TheEmployeeASignInWasAbout.cs) | `ISecuritySubjectLocator`: which subject a failed sign-in was about, so security entries on the trail are attributed |
| [`time-off/src/TimeOff.Host/Identity/FirstAdministrator.cs`](time-off/src/TimeOff.Host/Identity/FirstAdministrator.cs) | The first account, from configuration at start, through an operation run as an internal call |
| [`invoicing/src/Invoicing.Host/Program.cs`](invoicing/src/Invoicing.Host/Program.cs) | An external provider: `UseOidcAuthentication` with authority, audience, role and name claims |
