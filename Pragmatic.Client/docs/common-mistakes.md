# Common Mistakes

## 1. Missing AdditionalFiles registration

**Symptom**: The manifest file is in the project directory but no client code is generated.

```xml
<!-- WRONG: file exists but generator cannot see it -->
<ItemGroup>
  <None Include="PragmaticManifest.json" />
</ItemGroup>
```

```xml
<!-- CORRECT: registered as AdditionalFiles for the source generator -->
<ItemGroup>
  <AdditionalFiles Include="PragmaticManifest.json" />
</ItemGroup>
```

The source generator only reads files from the `AdditionalTextsProvider` pipeline. Regular `None` or `Content` items are invisible to it.

## 2. Using Private="true" with Mode B assembly references

**Symptom**: Mode B discovery does not find the manifest from the referenced assembly, or the domain DLL is unnecessarily included in the client output.

```xml
<!-- WRONG: domain DLL copied to output, or manifest not found -->
<ProjectReference Include="..\Showcase.Booking\Showcase.Booking.csproj" />
```

```xml
<!-- CORRECT: compile-only reference, DLL not copied -->
<ProjectReference Include="..\Showcase.Booking\Showcase.Booking.csproj"
                  Private="false"
                  ReferenceOutputAssembly="true" />
```

`Private="false"` prevents the domain assembly from being copied to the client's `bin/` output. The generator only needs it at compile time to read `[PragmaticMetadata]` attributes.

## 3. Forgetting the SourceGenerator package reference flags

**Symptom**: Build errors because the generator assembly is treated as a runtime dependency instead of an analyzer.

```xml
<!-- WRONG: generator DLL included as runtime dependency -->
<PackageReference Include="Pragmatic.Client.SourceGenerator" />
```

```xml
<!-- CORRECT: marked as analyzer, not referenced at runtime -->
<PackageReference Include="Pragmatic.Client.SourceGenerator"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Without `OutputItemType="Analyzer"`, the SG assembly is not loaded by the compiler as a generator. Without `ReferenceOutputAssembly="false"`, the SG DLL leaks into the runtime output.

## 4. Throwing exceptions instead of checking Result

**Symptom**: Unhandled `InvalidOperationException` when accessing `result.Value` on a failed result.

```csharp
// WRONG: accessing Value without checking -- throws if the call failed
var guest = (await client.GetGuest(id)).Value;
```

```csharp
// CORRECT: check IsSuccess before accessing Value
var result = await client.GetGuest(id);
if (result.IsSuccess)
{
    var guest = result.Value;
    // ...
}
else
{
    HandleError(result.Error);
}
```

Every generated client method returns `Result<T, IError>` or `VoidResult<IError>`. The whole point is to avoid exceptions for expected error cases. Always check `IsSuccess` or use pattern matching.

## 5. Expecting server domain types at runtime

**Symptom**: `ClassNotFoundException` or missing types because the client project tries to use the server's entity types directly.

The generated client produces its own DTO records (e.g., `GuestDto`) in the client namespace. These are local copies, not references to the server's types. You cannot cast between them.

```csharp
// WRONG: importing server namespace
using Showcase.Booking.Dtos;

// CORRECT: use the generated types in your client namespace
using MyApp.ApiClient; // namespace = client assembly name
```

If you need to share types, create a shared contracts project. But the default Pragmatic.Client design intentionally decouples client and server types.

## 6. Manifest file name does not match expected patterns

**Symptom**: Manifest file is registered as `AdditionalFiles` but the generator ignores it.

The generator looks for files ending in:
- `manifest.json` (e.g., `booking-manifest.json`, `my-api-manifest.json`)
- `PragmaticManifest.json`

```xml
<!-- WRONG: file name does not end with "manifest.json" -->
<AdditionalFiles Include="booking-api.json" />

<!-- CORRECT: ends with "manifest.json" -->
<AdditionalFiles Include="booking-manifest.json" />
```

## 7. Boundary filter typo generates nothing

**Symptom**: No client code is generated. The build reports `PRAG2302`.

```xml
<!-- WRONG: typo "Bookng" does not match any operationId prefix -->
<PropertyGroup>
  <PragmaticClientBoundaries>Bookng</PragmaticClientBoundaries>
</PropertyGroup>
```

The `PragmaticClientBoundaries` property filters endpoints by `operationId` prefix. If no endpoint
matches, the generator emits **PRAG2302** naming the filter it used, and produces no output. Match the
boundary name to the `operationId` prefix in the manifest: `"Booking.CreateGuest"` requires `"Booking"`.

## 7b. Ignoring PRAG2301: a client method typed as `object`

**Symptom**: a generated method returns `Result<object, IError>`, or takes an `object?` parameter, and
the build reports `PRAG2301` naming the type.

The generated client can only name types the manifest describes. Anything else degrades to `object`,
which compiles but leaves the caller without a typed payload. Expose the type as a DTO on the boundary
so it lands in the manifest, and the client picks it up on the next build.

Do not suppress PRAG2301 wholesale: it is the only signal that a "typed client" is not typed.

## 8. Handling ApiError.Extensions without null checks

**Symptom**: `NullReferenceException` when accessing extensions on an error.

```csharp
// WRONG: Extensions may be null
var traceId = apiError.Extensions["traceId"];

// CORRECT: null-safe access
if (apiError.Extensions?.TryGetValue("traceId", out var traceId) == true)
{
    Console.WriteLine($"Trace: {traceId}");
}
```

`ApiError.Extensions` is `IReadOnlyDictionary<string, object?>?` -- it can be null if the ProblemDetails response had no extension fields.

## 9. Expecting generic response types to be fully typed

**Symptom**: A method returns `Result<object, IError>` instead of `Result<PagedResult<GuestDto>, IError>`.

The current generator does not resolve generic wrapper types like `PagedResult<T>`. These map to `object`. This is a known limitation. Workaround: deserialize the response manually for paginated endpoints.

## 10. Multiple manifests with the same assembly name

**Symptom**: Only one manifest is processed, the other is silently skipped.

The generator deduplicates manifests by the `assembly` field. If two manifests declare the same `"assembly": "Showcase.Booking"`, only the first one (Mode A before Mode B) is processed.

Ensure each manifest has a unique `assembly` value, or consolidate them into a single manifest.
