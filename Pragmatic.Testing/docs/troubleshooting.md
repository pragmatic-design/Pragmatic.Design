# Troubleshooting

## No tests were generated at all

Check, in order:

1. **The generator is referenced as an analyzer**, not as a plain library:
   ```xml
   <ProjectReference Include="...\Pragmatic.Testing.SourceGenerator.csproj"
                     OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
   ```
2. **The app is reachable from the test compilation.** The generator looks for
   `Pragmatic.Endpoints.Attributes.EndpointAttribute` in the compilation; if the test project does not
   reference the module containing the endpoints (directly or transitively), nothing is emitted.
3. **The endpoints are implemented.** Bodies that are still `throw Behavior.Pending()` are skipped.

Inspect what was produced by enabling `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>`
and looking under `obj/.../generated/Pragmatic.Testing.SourceGenerator/`.

## `PragmaticContractHost.Client was not set`

The collection fixture is missing or is not attached to the right collection. It must be declared for
`PragmaticContractHost.Collection` (the literal string `"PragmaticContractTests"`):

```csharp
[CollectionDefinition(PragmaticContractHost.Collection)]
public sealed class ContractTestCollection : ICollectionFixture<ContractAppFixture>;
```

and the fixture must assign `PragmaticContractHost.Client` inside `InitializeAsync`.

## Every authorization contract fails with 401

The host is not resolving the dev-identity headers. `HeaderUserMiddleware` runs only in the
Development environment and must be registered **before** `UseAuthentication()`. Confirm the test host
sets `ASPNETCORE_ENVIRONMENT=Development`; outside Development the middleware throws by design.

## A `_WithRequiredPermission_IsReachable` test fails with 400

The endpoint requires a request body and the generated contract sends none: the assertion is that
authorization did not block the call, and a 400 means it did not. This is a genuine failure only if your
pipeline maps a missing body to 401/403; otherwise it indicates the endpoint rejects before
authorization, which the wider `ShouldNotBeForbidden` assertion already tolerates. If you see 401/403,
check that the permission string on the endpoint matches the one the host evaluates.

## A `_WithoutRequiredPermission_IsRejected` test fails with 2xx

The endpoint is not enforcing its permission. This is the failure the contract exists to surface: an
unprivileged caller reached it and succeeded. Verify the endpoint's `[RequirePermission]` (or its
derived permission) is actually evaluated by the pipeline for that route.

## A CRUD success test is missing for an endpoint

Its request body contains a foreign key, a strongly-typed id, or a nested complex type, which the
synthesizer cannot produce a valid literal for. The validation test is still generated. Cover the
success path by hand with the typed client.

## Cross-tenant isolation test fails

The entity is not tenant-scoped, or the tenant header name differs from the one the host reads. The
contract creates an entity under one tenant and expects 404 when reading it as another; a 200 means the
tenant filter is not applied to that entity.

## Tests are slow to appear in the IDE

The generator scans every type in every referenced non-framework assembly on each compilation change,
because endpoints can live in any referenced module. Reducing the number of module references a test
project pulls in is the effective lever.

## Generated tests disappeared after a rename

Contract tests key off boundary names derived from the endpoint metadata. Renaming a boundary renames
the generated class (`{Boundary}AuthContractTests`), so a filter or a `[Collection]` reference pinned to
the old name will match nothing.
