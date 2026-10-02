---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Patch. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Patch/docs/troubleshooting.md
sidebar:
  order: 5
---
Practical problem/solution guide for Pragmatic.Patch. Each section covers a common issue, the likely causes, and the fix.

---

## PRAG2200: Type Must Be Partial

Your patch type fails to compile with diagnostic PRAG2200.

### Fix

Add the `partial` keyword:

```csharp
// Wrong
[GeneratePatch<Guest>]
public record UpdateGuestPatch;

// Right
[GeneratePatch<Guest>]
public partial record UpdateGuestPatch;
```

The source generator emits properties, methods, and a JSON converter into a partial class. Without `partial`, the compiler cannot merge the generated code with your declaration.

---

## PRAG2201: Entity Type Could Not Be Resolved

The generator reports that the entity type in `[GeneratePatch<TEntity>]` could not be resolved.

### Checklist

1. **Is the entity type accessible?** The entity class must be in a referenced assembly or the same project. Check that the project reference exists in your `.csproj`.

2. **Is the entity type a class?** `[GeneratePatch]` requires a class. Records, structs, and interfaces are not supported as entity types.

3. **Is the source generator analyzer referenced?** Verify the Pragmatic.SourceGenerator is referenced with `OutputItemType="Analyzer"`:

   ```xml
   <ProjectReference Include="..\Pragmatic.SourceGenerator\Pragmatic.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
   ```

---

## PRAG2202: No Settable Properties

The generator warns that the entity has no properties suitable for patching.

### Checklist

1. **Does the entity have writable properties?** The generator looks for properties with either a public setter or a `Set*()` method:

   ```csharp
   // No settable properties -- PRAG2202
   public class Immutable { public string Name { get; } }

   // Has settable properties -- works
   public class Mutable { public string Name { get; set; } }

   // Also works -- Set* method
   public class Encapsulated
   {
       public string Name { get; private set; }
       internal void SetName(string value) => Name = value;
   }
   ```

2. **Are all properties in the excluded list?** If the entity only has `Id`, auditing, and soft delete properties, all of them are excluded and nothing is left to patch.

---

## All Optional Properties Are Undefined After Deserialization

You send a PATCH request with values, but all `Optional<T>` properties remain `Undefined` in the handler.

### Checklist

1. **Is the patch type SG-generated?** Only types annotated with `[GeneratePatch<TEntity>]` get a generated JSON converter. If you created a manual DTO with `Optional<T>` properties, register the fallback converter:

   ```csharp
   builder.Services.ConfigureHttpJsonOptions(options =>
       options.SerializerOptions.Converters.Add(new OptionalConverterFactory()));
   ```

2. **Is `Content-Type` set to `application/json`?** Without the correct content type, ASP.NET Core may not invoke the JSON deserializer.

3. **Are property names matching?** The generated converter respects `PropertyNamingPolicy`. If your API uses camelCase but you send PascalCase (or vice versa), the converter handles both. But check for typos in property names.

4. **Is the request body valid JSON?** Malformed JSON may fail silently. Check the response status code -- a 400 indicates a deserialization error.

---

## ApplyTo Does Nothing

You call `patch.ApplyTo(entity)` but no properties change on the entity.

### Checklist

1. **Did the client send any fields?** Check `patch.ModifiedProperties` -- if it is empty, the JSON body was `{}` or all fields were absent.

2. **Are the property names correct in the JSON?** If the JSON uses `"first_name"` but the property is `FirstName`, the converter won't match (unless you have a custom naming policy configured).

3. **Is the entity the correct type?** `ApplyTo()` is strongly typed. Verify you are passing the entity type specified in `[GeneratePatch<TEntity>]`.

4. **Are the properties excluded?** Check if the properties you expect to patch are in the excluded list (Id, auditing, soft delete, collections, entity references). See [Concepts: Excluded Properties](/modules/patch/concepts/#excluded-properties).

---

## Null Sent in JSON But Property Stays Undefined

You send `{ "price": null }` for a `decimal` property, but `Price` remains `Undefined` instead of `Null`.

### Cause

`decimal` is a non-nullable value type. It cannot represent `null`, so the converter treats `null` as "not applicable" and leaves the property as `Undefined`.

### Fix

If you need to support null values for a property, make the entity property nullable:

```csharp
public class Product
{
    // Non-nullable: null in JSON becomes Undefined
    public decimal Price { get; set; }

    // Nullable: null in JSON becomes Optional<decimal?>.Null
    public decimal? DiscountRate { get; set; }
}
```

For `decimal Price`, the only way to set it is to send an actual value: `{ "price": 0 }` produces `Optional.Of(0m)`.

---

## ModifiedProperties Contains Unexpected Entries

`ModifiedProperties` includes a field that the client did not send.

### Checklist

1. **Check the raw JSON.** The generated converter tracks keys by their presence in the JSON object, not by their value. Even `{ "name": null }` adds `"Name"` to `ModifiedProperties`.

2. **Check for default serialization behavior.** Some JSON serializers include properties with default values. Ensure the client is configured to omit undefined fields.

3. **Check the naming policy.** The converter normalizes property names through the configured `PropertyNamingPolicy`. Both `"name"` and `"Name"` resolve to the same property.

---

## Generated Code Not Appearing in IDE

IntelliSense does not show the generated properties or `ApplyTo()` method.

### Checklist

1. **Restart the IDE.** Source generators sometimes require a restart for the language server to pick up new outputs.

2. **Build the project.** Run `dotnet build` to ensure the generator executes. Check the build output for errors.

3. **Check the analyzer reference.** Ensure the Pragmatic.SourceGenerator is referenced correctly:

   ```xml
   <ProjectReference Include="..\Pragmatic.SourceGenerator\Pragmatic.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
   ```

4. **Check generated files.** Look in `obj/Debug/net10.0/generated/` for the generated `.g.cs` files. They should include `{TypeName}.Patch.g.cs` and `{TypeName}.JsonConverter.g.cs`.

---

## FAQ

### Can I use Optional\<T\> outside of generated patch DTOs?

Yes. `Optional<T>` is a public struct in the `Pragmatic.Patch` namespace. You can use it in any DTO, but you must register an `OptionalConverter<T>` for each `T` for JSON serialization:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new OptionalConverter<string>()));
```

Note: there is no open-generic factory (it would need `MakeGenericType`), and `OptionalConverter<T>` is itself annotated `[RequiresDynamicCode]`. For NativeAOT, use a generated patch type.

### Can I customize which properties are excluded?

Not currently. The exclusion list is hardcoded in the generator (identity, auditing, soft delete, concurrency, navigation, entity references). If you need to exclude additional properties, use a separate entity type that omits them, or apply the patch selectively:

```csharp
if (patch.SensitiveField.HasValue)
    return ValidationError.For("SensitiveField", "patch.field_not_patchable");

patch.ApplyTo(entity);
```

### Can I use [GeneratePatch] with records?

Yes. Both `partial record` and `partial class` are supported. Records are recommended because they provide value equality and `with` expressions out of the box.

### How does this work with Pragmatic.Endpoints?

Declare the patch as a `[FromBody]` property on the endpoint:

```csharp
[Endpoint(HttpVerb.Patch, "/api/products/{productId}")]
public partial class PatchProductEndpoint : Endpoint<ProductDto>
{
    [FromRoute] public required Guid ProductId { get; init; }
    [FromBody] public required PatchProduct Patch { get; init; }

    public override async Task<Result<ProductDto, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(ProductId, ct);
        if (product is null) return NotFoundError.For("Product", ProductId.ToString());

        Patch.ApplyTo(product);
        await _uow.SaveChangesAsync(ct);
        return ProductDto.FromEntity(product);
    }
}
```

---

## Getting Help

1. Check the [concepts guide](/modules/patch/concepts/) for architecture overview
2. Check the [getting started guide](/modules/patch/getting-started/) for setup instructions
3. Check the [tri-state semantics guide](/modules/patch/tri-state-semantics/) for `Optional<T>` details
4. Open an issue on the Pragmatic.Design repository with:
   - Your entity class definition
   - The `[GeneratePatch]` declaration
   - The JSON body you are sending
   - The expected vs actual behavior
   - .NET version
