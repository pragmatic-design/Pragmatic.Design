# Troubleshooting

Practical problem/solution guide for Pragmatic.Endpoints. Each section covers a common issue, the likely causes, and the fix.

---

## Endpoint Not Registering (404 on All Routes)

Your endpoint class compiles, but every request returns 404.

### Checklist

1. **Is the class `partial`?** The SG cannot generate the handler without `partial`. You will see diagnostic PRAG0500 if this is missing.

2. **Does it have a recognised shape?** The class must inherit from `Endpoint<T>`, `VoidEndpoint`, `StreamingEndpoint<T>`, `DomainAction<T>`, `VoidDomainAction`, `StreamingDomainAction<T>` or `Mutation<T>`, or carry `[Query<TEntity, TResult>]`. Without this, the SG skips it entirely (PRAG0501).

3. **Does it have the `[Endpoint]` attribute?** The SG only processes classes decorated with `[Endpoint(HttpVerb.Get, "/route")]`. A missing attribute means no code generation.

4. **Did you call `AddPragmaticEndpoints()` in Program.cs?**

   ```csharp
   builder.Services.AddPragmaticEndpoints();
   ```

5. **Did you call `MapPragmaticEndpoints()` after building the app?**

   ```csharp
   var app = builder.Build();
   app.MapPragmaticEndpoints();
   ```

6. **Is the SG analyzer referenced correctly?** In your `.csproj`, the Pragmatic.SourceGenerator must be referenced with `OutputItemType="Analyzer"`:

   ```xml
   <ProjectReference Include="..\Pragmatic.SourceGenerator\Pragmatic.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
   ```

### If Using Groups

- Does the `[EndpointGroup]` class exist? It cannot be `static`: it is named as a type argument, and a static class is refused there (CS0718).
- Is the membership declared on the endpoint? It is the same attribute with one type argument, pointing at a class decorated with `[EndpointGroup]`:

  ```csharp
  [Endpoint(HttpVerb.Get, "/")]
  [EndpointGroup<OrdersGroup>]
  ```

- Diagnostic PRAG0507 fires when the type named as the group does not exist, or exists without `[EndpointGroup]`; the message says which. A group declared in a referenced assembly is found like any other.

---

## Route Gives 404 But Endpoint Exists

The endpoint registers (no build errors, no diagnostics), but the specific URL returns 404.

### Possible Causes

**Route prefix stacking.** The final route is composed from three layers:

| Layer | Source | Example |
|-------|--------|---------|
| Global prefix | `PragmaticEndpointsOptions.RoutePrefix` | `/api` |
| Group prefix | `[EndpointGroup("/v1/orders")]` | `/v1/orders` |
| Endpoint route | `[Endpoint(HttpVerb.Get, "/{id}")]` | `/{id}` |
| **Final** | | `/api/v1/orders/{id}` |

If you are requesting `/v1/orders/123` but the global prefix is `/api`, the actual route is `/api/v1/orders/123`.

**Missing leading slash.** Routes should start with `/`. While the SG normalizes this, inconsistent slashes can cause confusion when debugging.

**Route parameter constraint mismatch.** If your route uses `{id:guid}` but you pass an integer, ASP.NET Core will not match the route. Verify the constraint matches the actual parameter type:

```csharp
// Route says guid, but caller sends an integer -> 404
[Endpoint(HttpVerb.Get, "/orders/{id:guid}")]
```

**HTTP verb mismatch.** Sending a POST to a GET endpoint returns 404 (or 405 Method Not Allowed). Verify the verb in the attribute matches the request.

---

## Pre-Processor Not Running

The `[PreProcessor<T>]` attribute is present, but the processor logic never executes.

### Checklist

1. **Check the `Order` property.** Lower values run first. If your processor depends on another processor's side effects, verify the ordering:

   ```csharp
   [PreProcessor<AuthCheckProcessor>(Order = 0)]
   [PreProcessor<ValidateGuestProcessor>(Order = 1)]
   ```

2. **DI registration is generated.** `TryAddScoped<TProcessor>` is emitted by the generator, in the
   assembly's `AddPragmaticEndpoints()` and in the Composition host's `RegisterAllEndpoints()`; there
   is nothing to write. The one type it cannot register is one the container cannot build (abstract,
   or without a public constructor), and `PRAG0534` reports that at compile time.

3. **Verify the interface.** The processor must implement `IEndpointPreProcessor` or `IEndpointPreProcessor<TEndpoint>`. If neither is implemented, the attribute does not compile (`CS0311`).

4. **Check for short-circuiting.** A preceding pre-processor may be returning `PreProcessorResult.Fail(...)`, which stops the pipeline before your processor runs.

---

## Rate Limiting Not Working

The `[RateLimit]` attribute is on the endpoint, but requests are never throttled.

### Checklist

1. **Did you add the rate limiting middleware?**

   ```csharp
   app.UseRateLimiter();   // Required -- must be before MapPragmaticEndpoints()
   app.MapPragmaticEndpoints();
   ```

   Without `UseRateLimiter()`, the rate limiting metadata on endpoints has no effect.

2. **Is the policy name matching?** Inline `[RateLimit(Requests = 5, Window = "1m")]` generates a policy named `__pragmatic_ratelimit_{TypeName}`. If you use `[RateLimit(Policy = "standard")]`, verify the policy is registered:

   ```csharp
   services.AddPragmaticEndpoints(options =>
   {
       options.ConfigureRateLimiter("standard", limiter =>
       {
           limiter.PermitLimit = 100;
           limiter.Window = TimeSpan.FromMinutes(1);
       });
   });
   ```

3. **For distributed rate limiting:** Is `ICacheStack` registered via `Pragmatic.Caching`? In-memory rate limiters are per-instance, so multiple app instances each maintain separate counters.

4. **Invalid configuration.** A `[RateLimit]` with neither `Policy` nor both `Requests` and `Window` specified has no effect at runtime.

---

## File Upload Returning 413 or 415

### 413 Payload Too Large

The uploaded file exceeds the size limit. Check in this order:

1. **`[MaxFileSize]` on the property.** The SG validates the file size before `HandleAsync`.
2. **`MaxUploadFileSize` in `PragmaticEndpointsOptions`.** Applied to `IFormFile` properties without an explicit `[MaxFileSize]`.
3. **Kestrel limit.** ASP.NET Core's default request body size is ~28.6 MB. For larger uploads:

   ```csharp
   builder.WebHost.ConfigureKestrel(options =>
   {
       options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100 MB
   });
   ```

### 415 Unsupported Media Type

The file's MIME type is not in the allowed list. Check:

1. **`[AllowedContentTypes]` on the property.** Only the listed types are accepted.
2. **`DefaultAllowedContentTypes` in `PragmaticEndpointsOptions`.** Applied when no explicit attribute is present.
3. **Client-reported content type.** The `Content-Type` header is set by the client. Verify the client sends the correct type. Note: `[AllowedContentTypes]` checks the header, not the file bytes.

---

## Validation Not Running

The endpoint or action has validation attributes (`[Required]`, `[Email]`, `[MinLength]`), but invalid input is not rejected.

### Checklist

1. **Is `Pragmatic.Validation` referenced?** The validation SG generates `ISyncValidator<T>` implementations only when the package is present in the project.

2. **Are validation attributes on the correct properties?** Attributes must be on the endpoint's public properties, not on nested DTO types.

3. **For DomainAction endpoints:** Validation runs automatically before `Execute()` when the SG detects a validator. If validation is not triggering, check that the generated validator exists in the SG output.

4. **For plain Endpoint<T>:** Ensure the generated handler calls the validator. The SG integrates validation when it detects `ISyncValidator` or `IAsyncValidator` for the endpoint type.

---

## Authorization Returns 401 on All Requests

Every request fails with 401 Unauthorized, including endpoints that should be public.

### Checklist

1. **Is `RequireAuthorizationByDefault` enabled?** When set to `true` (globally or on a group), all endpoints require authentication. Exempt specific endpoints with `[AllowAnonymous]`.

2. **Is the auth middleware configured?**

   ```csharp
   app.UseAuthentication();
   app.UseAuthorization();
   app.MapPragmaticEndpoints();
   ```

   Both must be in the pipeline and in the correct order (authentication before authorization).

3. **Is the authentication scheme configured?** ASP.NET Core needs at least one authentication scheme registered:

   ```csharp
   builder.Services.AddAuthentication("Bearer")
       .AddJwtBearer(options => { /* ... */ });
   ```

4. **Group-level authorization.** Check if the endpoint's group has `RequireAuthorization = true` in `ConfigureGroup`:

   ```csharp
   options.ConfigureGroup("Orders", group =>
   {
       group.RequireAuthorization = true; // All endpoints in this group require auth
   });
   ```

---

## Generated Code Not Compiling

The build fails with PRAG05xx diagnostics from the source generator.

### Diagnostics Reference

| ID | Severity | Cause | Fix |
|----|----------|-------|-----|
| PRAG0500 | Error | Class is not `partial` | Add `partial` keyword to the class declaration |
| PRAG0501 | Error | Not a recognised shape | Inherit from `Endpoint<T>`, `VoidEndpoint`, `StreamingEndpoint<T>`, `DomainAction<T>`, `VoidDomainAction`, `StreamingDomainAction<T>` or `Mutation<T>`, or carry `[Query<TEntity, TResult>]`; the message lists them |
| PRAG0502 | Error | Route not specified | Add a route pattern to the `[Endpoint]` attribute: `[Endpoint(HttpVerb.Post, "/orders")]` |
| PRAG0503 | Error | More than 6 error types | Reduce the generic error type parameters to 6 or fewer |
| PRAG0504 | Warning | Route parameter has no matching property | Add a public property matching the route parameter name, or fix the spelling |
| PRAG0505 | Error | Duplicate endpoint name | Give each endpoint a unique `[Endpoint(Name = "...")]` |
| PRAG0507 | Error | Endpoint group not found | The type in `[EndpointGroup<X>]` must exist and be decorated with `[EndpointGroup]`; the message says which of the two is missing |
| PRAG0512 | Info | Implicit body binding | Add `[FromBody]`, `[FromQuery]`, or `[FromRoute]` for clarity |
| PRAG0531 | Error | `[ReturnsDto<T>]` names a DTO that cannot be built from the mutation's entity | Add `[MapFrom<TEntity>]` to the DTO: the handler calls its generated `FromEntity`. A `{Entity}ReadDto` scaffolded by `[Resource]` needs nothing |
| PRAG0532 | Error | An operation exposed as GET has a nested object among its properties | A query string carries scalars only: make it a scalar, take it as JSON in a single value, or expose the operation on a verb with a body |
| PRAG0533 | Error | A create answers with a DTO that reads through a navigation to another aggregate | A create has no loaded entity to read it from, and `[EagerLoad]` has no query to attach to: answer with a DTO of this aggregate alone, or read the fuller shape back with a query |
| PRAG0534 | Error | Processor cannot be constructed by the container | Name a concrete processor with a public constructor: the generated registration cannot cover an abstract type or one with no public constructor |
| PRAG0535 | Error | `[ReturnsDto<T>]` beside `ReturnType = Id` or `LogicalKey` | The key answers, and the DTO applies only to a mutation that returns the entity: remove one of the two |
| PRAG0536 | Error | An optional header, query, claim or cookie value on an `init` property with a non-constant initializer | The generated endpoint repeats the default in the object initializer: make it a constant, or give the property a `set` accessor |
| PRAG0537 | Warning | An error declares `[HttpStatus(n)]` and its own `StatusCode` answers another | Make the two agree: the attribute is what the contract documents, `StatusCode` is what the caller receives |
| PRAG0538 | Info | A published type declares `TenantId`, `OwnerId`, `AccessScopes`, `RowVersion` or `PersistenceId`, which no response carries | Rename the member or drop it from the shape the endpoint answers with: a client generated from the contract would read a silent default |
| PRAG0515 | Error | Autocomplete entity missing key | Add an `Id` property or `[Key]` attribute to the entity |
| PRAG0516 | Error | Invalid `[MaxFileSize]` limit | Use a positive byte limit (a value ≤ 0 would reject every upload) |
| PRAG0550 | Error | `[Autocomplete]` on non-string | Move `[Autocomplete]` to a `string` property |
| PRAG0551 | Warning | Versioned methods need `Asp.Versioning.Http` | Add the `Asp.Versioning.Http` NuGet package |

Check the **Error List** window in Visual Studio or the build output for diagnostic details and the affected source location.

---

## Body DTO Not Binding

The request body is sent as JSON, but properties on the endpoint are `null` or default.

### Checklist

1. **Properties must be public with `{ get; init; }` or `{ get; set; }`.** Private or internal properties are not bound from the request body.

2. **Use the `required` keyword for mandatory properties.** This ensures the model binder rejects requests with missing fields:

   ```csharp
   public required string Name { get; init; }
   ```

3. **JSON property names are camelCase by default.** A property named `CustomerId` expects `"customerId"` in the JSON body. Verify the casing matches.

4. **Check the `Content-Type` header.** The request must include `Content-Type: application/json`. Without it, ASP.NET Core will not attempt JSON deserialization.

5. **Binding source conflicts.** If a property has `[FromRoute]`, `[FromQuery]`, or `[FromHeader]`, it is not bound from the body. Properties without explicit binding attributes are auto-detected as body properties.

6. **Form vs. JSON conflict.** When `[FromForm]` properties exist on the endpoint, no body DTO is generated. Form data and JSON body are mutually exclusive.

---

## Endpoint Returns 500 Unexpectedly

The endpoint should return a business error but returns 500 Internal Server Error.

### Common Causes

1. **Unhandled exception in `HandleAsync` or `Execute`.** The Result pattern only works when you return error objects, not when exceptions are thrown. Wrap risky operations:

   ```csharp
   // Instead of letting exceptions propagate:
   var invoice = await _invoices.GetByIdAsync(Id, ct);
   if (invoice is null)
       return NotFoundError.For("Invoice", Id);
   ```

2. **DI resolution failure.** A dependency field on the endpoint is `null!` at runtime because the service is not registered. Check that all private fields have corresponding DI registrations.

3. **Missing middleware.** Add `app.UseExceptionHandler()` to convert unhandled exceptions into ProblemDetails responses instead of raw 500s.

---

## OpenAPI Schema Missing or Incomplete

Swagger UI shows the endpoint but without request/response schemas.

### Checklist

1. **Add `[EndpointSummary]` and `[EndpointDescription]`** for human-readable documentation.
2. **Add `[ApiTags("Orders")]`** to group endpoints in the Swagger UI.
3. **Use `[HttpStatus(201)]`** to override the default 200 success status code.
4. **Add XML doc comments** on public properties for property-level documentation in the generated body DTO.
5. **Multiple error types** declared in the generic parameters (`DomainAction<T, NotFoundError, ValidationError>`) automatically produce response schema entries for each error's status code.

### The runtime document shows a response as JSON with no schema

The log says `The runtime OpenAPI document describes {Endpoint} without the schema of {Type}`.

**Cause:** ASP.NET's runtime document (`MapOpenApi`) reads each schema back through a writer limited to
the application's `JsonSerializerOptions.MaxDepth`, 64 by default, and a type past it would fail the
whole document with a 500. An entity with navigations is the usual case. The type is left out of that
one document instead.

**Fix:** answer with a DTO rather than the entity. Do not raise `MaxDepth` for this: it is the same
setting that bounds how deeply a request body may nest. The compile-time document
(`MapPragmaticOpenApi`) describes the type in full either way.

### Two hosts in one process, and one of them describes the other's API

**Cause:** a host that registers neither `HostOpenApiDocument` nor `HostManifest` falls back to
process-wide statics. The compile-time document static (`PragmaticOpenApiRegistry`) is written by a
`[ModuleInitializer]` where last writer wins, so the host loaded second answers for both. The runtime
document's enrichment falls back to the **manifest** registry, which accumulates instead: two hosts do
not lose a manifest, they share both, so the endpoint lookup spans the process. That one is subtler,
because the enrichment is driven by the document's own operations and another host's entries are never
reached by a route this host does not serve, but `requiresAuthentication`, which decides whether the
document declares security schemes at all, is computed over the whole lookup.

**Fix:** a generated host registers `HostOpenApiDocument` and `HostManifest` in **its own**
container, and both readers prefer them. The statics are the fallback for a host that registers
neither, which is why a single-host application needs nothing. If you host two Pragmatic applications
in one process and see one describing the other, their generated code does not carry those
registrations: rebuild the hosts with the current generator.

---

## FAQ

### Can I use both `[FromForm]` and body properties on the same endpoint?

No. When `[FromForm]` properties are present, the SG does not generate a `[FromBody]` DTO. Form data and JSON body binding are mutually exclusive.

### How do I change the success HTTP status code?

Use `[HttpStatus(201)]` on the endpoint class. The default is 200 for `Endpoint<T>` and 204 for `VoidEndpoint`.

### Why does my route parameter property stay at default value?

Verify three things: (a) the property name matches the route parameter name (case-insensitive), (b) the property has `[FromRoute]`, and (c) the type is compatible with the route constraint (e.g., `Guid` for `{id:guid}`).

### Can I override group-level settings on a single endpoint?

Yes. Endpoint-level attributes take priority over group-level configuration. For example, an `[AllowAnonymous]` attribute on an endpoint overrides the group's `RequireAuthorization = true`.

### How do I debug what routes were registered?

Inspect the generated `MapPragmaticEndpoints()` method in the SG output. In Visual Studio, expand **Dependencies > Analyzers > Pragmatic.SourceGenerator** in Solution Explorer to see all generated files. Look for the endpoint registration file.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working endpoint implementations across all base class types.
- **Binding Reference**: See [binding-reference.md](binding-reference.md) for all binding scenarios.
- **Error Handling**: See [error-handling.md](error-handling.md) for HTTP status mapping and custom errors.
- **Endpoint Groups**: See [endpoint-groups.md](endpoint-groups.md) for route prefix composition and group configuration.
