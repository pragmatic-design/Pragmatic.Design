# Getting Started with Pragmatic.Actions

This guide walks you through creating your first DomainAction and Mutation, wiring them into DI, and invoking them.

## Prerequisites

- .NET 10.0+
- `Pragmatic.Actions` package
- `Pragmatic.SourceGenerator` analyzer reference
- `Pragmatic.Result` (comes transitively)

Your project file should include:

```xml
<ItemGroup>
  <PackageReference Include="Pragmatic.Actions" />
  <ProjectReference Include="..\Pragmatic.SourceGenerator\...\Pragmatic.SourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

---

## Step 1: Define a Boundary

A boundary groups your actions into a module. Create a boundary class in your project's root namespace:

```csharp
using Pragmatic.Actions.Attributes;

namespace MyApp.Catalog;

[Boundary]
public partial class CatalogBoundary;
```

The source generator will capture all `[DomainAction]` and `[Mutation]` classes whose namespace starts with `MyApp.Catalog` and generate an `ICatalogActions` interface.

---

## Step 2: Create a DomainAction

A `DomainAction<TReturn>` is a self-contained operation that returns a typed result.

```csharp
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;

namespace MyApp.Catalog.Products.Actions;

[DomainAction]
public partial class GetProductById : DomainAction<ProductDto>
{
    // Private fields are treated as dependencies -- resolved from DI
    private IReadRepository<Product> _products;

    // Public properties are treated as inputs -- set by the caller
    public required Guid Id { get; init; }

    public override async Task<Result<ProductDto, IError>> Execute(CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(Id, ct).ConfigureAwait(false);
        if (product is null)
            return new NotFoundError { EntityType = "Product", EntityId = Id.ToString() };

        return product.ToDto();
    }
}
```

Key points:
- The class must be `partial` (the SG generates the other part).
- Private fields without initializers are auto-detected as dependencies.
- The `Execute` method returns `Result<TReturn, IError>` -- never throw for business errors.
- Namespace determines boundary assignment: `MyApp.Catalog.Products.Actions` maps to `CatalogBoundary`.

---

## Step 3: Create a Mutation

A `Mutation<TEntity>` handles entity CRUD through a structured pipeline. The simplest mutations need no `Execute` or `ApplyAsync` override.

### Create Mutation

```csharp
using Pragmatic.Actions.Mutation;

namespace MyApp.Catalog.Products.Mutations;

[Mutation(Mode = MutationMode.Create)]
public partial class CreateProductMutation : Mutation<Product>
{
    public required string Name { get; init; }
    public required decimal Price { get; init; }
    public string? Description { get; init; }
}
```

The source generator:
1. Detects that `Product` has `SetName()`, `SetPrice()`, and `SetDescription()` methods
2. Generates `ApplyToEntity()` that calls `entity.SetName(this.Name)`, etc.
3. Generates a `MutationInvoker` nested class with the full pipeline

### Update Mutation

```csharp
[Mutation(Mode = MutationMode.Update)]
public partial class UpdateProductMutation : Mutation<Product>
{
    public required Guid Id { get; init; }      // Used to load the entity
    public string? Name { get; init; }           // Nullable = optional update
    public decimal? Price { get; init; }
}
```

For updates, the invoker loads the entity by `Id`, then applies only the non-null properties.

### Delete Mutation

```csharp
[Mutation(Mode = MutationMode.Delete)]
public partial class DeleteProductMutation : Mutation<Product>
{
    public required Guid Id { get; init; }
}
```

If the entity implements `ISoftDelete`, the invoker performs a soft-delete (sets `IsDeleted = true`). Otherwise, it removes the entity from the repository.

### Mode Inference

If you don't set `Mode` explicitly, it is inferred from the class name prefix:
- `CreateProduct...` -> `MutationMode.Create`
- `UpdateProduct...` -> `MutationMode.Update`
- `DeleteProduct...` -> `MutationMode.Delete`

If no prefix matches, the SG emits a diagnostic error.

---

## Step 4: Register Services

In your startup or `IStartupStep`:

```csharp
services.AddPragmaticActions();
```

This registers the pipeline filters (validation, authorization, logging) and the `ActionCallContext`.

The source generator also produces a registration extension for your module's actions:

```csharp
// Generated: _Infra.Actions.Registration.g.cs
services.AddMyAppCatalogActions(); // registers all invokers in the assembly
```

---

## Step 5: Invoke an Action

### From an Endpoint (Generated)

If your action has `[Endpoint]`, the SG generates an HTTP endpoint that creates the action, populates it from the request, and invokes it through the pipeline. No extra code needed:

```csharp
[DomainAction]
[Endpoint(HttpVerb.Get, "api/v1/products/{id}")]
public partial class GetProductById : DomainAction<ProductDto> { /* ... */ }
```

### From Application Code

Inject the invoker interface and call `InvokeAsync`:

```csharp
public class ProductService(IDomainActionInvoker<GetProductById, ProductDto> getProduct)
{
    public async Task<ProductDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var action = new GetProductById { Id = id };
        var result = await getProduct.InvokeAsync(action, ct);
        return result.IsSuccess ? result.Value : null;
    }
}
```

For mutations:

```csharp
public class ProductService(IMutationInvoker<CreateProductMutation, Product> createProduct)
{
    public async Task<Guid> CreateAsync(string name, decimal price, CancellationToken ct)
    {
        var mutation = new CreateProductMutation { Name = name, Price = price };
        var result = await createProduct.InvokeAsync(mutation, ct);
        return result.Value.Id;
    }
}
```

---

## Step 6: Add Validation

### Sync Validation (Attributes)

Add validation attributes directly to input properties. The `ValidationFilter` runs these automatically (no extra attribute needed):

```csharp
[DomainAction]
public partial class CreateUser : DomainAction<Guid>
{
    [Required, Email]
    public required string Email { get; init; }

    [Required, MinLength(2)]
    public required string Name { get; init; }
}
```

### Async Validation

For an async check (e.g., uniqueness against the database), write an `IAsyncValidator<CreateUser>` marked
`[Validator]`. The generator registers it and, because it now exists in the compilation, makes the
pipeline run it after the sync rules. No attribute on the action:

```csharp
[Validator]
public sealed class UniqueEmail(IUserDirectory directory) : IAsyncValidator<CreateUser>  // your service
{
    public async Task<ValidationError> ValidateAsync(CreateUser action, CancellationToken ct = default)
        => await directory.IsEmailTakenAsync(action.Email, ct)
            ? ValidationError.For(nameof(CreateUser.Email), "user.email_taken")
            : ValidationError.Valid;
}
```

`[Validate]` exists to override that inference explicitly: `[Validate(AsyncOnly = true)]` to skip the
sync rules, for instance.

### Mutation Validation

Mutations have two validation levels:
- **L1 (Input)**: Validates the mutation itself (same as DomainAction)
- **L2 (Entity)**: Validates the entity after changes are applied, with awareness of which properties changed

Both levels run automatically. Register `IValidator<TEntity>` in DI for L2 validation.

---

## Step 7: Add Authorization

```csharp
[DomainAction]
[RequirePermission("catalog.product.create")]
public partial class CreateProductAction : DomainAction<Guid> { /* ... */ }
```

The `PermissionAuthorizationFilter` checks that `ICurrentUser.Authorization.HasAllPermissions(...)` passes before `Execute()` runs. If the user lacks the permission, the pipeline short-circuits with a `ForbiddenError`.

For more complex rules, use policy-based authorization:

```csharp
[DomainAction]
[RequirePolicy<ProductManagementPolicy>]
public partial class CreateProductAction : DomainAction<Guid> { /* ... */ }
```

---

## Step 8: Add an Endpoint (Optional)

With `Pragmatic.Endpoints` referenced, add `[Endpoint]` to expose your action as an HTTP endpoint:

```csharp
[DomainAction]
[Endpoint(HttpVerb.Post, "api/v1/products")]
[ApiSummary("Create Product")]
[ApiTags("Products")]
[RequirePermission("catalog.product.create")]
public partial class CreateProductAction : DomainAction<Guid>
{
    private IRepository<Product> _products;

    public required string Name { get; init; }
    public required decimal Price { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct)
    {
        var product = Product.Create(Name, Price);
        _products.Add(product);
        return product.Id;
    }
}
```

The SG generates a minimal API endpoint that:
- Binds the request body to the action's public properties
- Invokes the action through the full pipeline
- Returns the appropriate HTTP status code and response

---

## VoidDomainAction Example

For operations that succeed or fail but return no value:

```csharp
[DomainAction]
[Endpoint(HttpVerb.Post, "api/v1/products/{id}/archive")]
public partial class ArchiveProduct : VoidDomainAction<NotFoundError>
{
    private IRepository<Product> _products;

    public required Guid Id { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(Id, ct);
        if (product is null)
            return NotFoundError.For<Product, Guid>(Id);

        product.Archive();
        return Success;  // convenience property on VoidDomainAction
    }
}
```

---

## What the Source Generator Produces

For each `[DomainAction]` class, the SG generates:

| Generated Artifact | Purpose |
|-------------------|---------|
| `{Type}.SetDependencies.g.cs` | Internal method that resolves private fields from DI |
| `{Type}.Invoker.g.cs` | Nested `Invoker` class extending `DomainActionInvoker<T, TReturn>` |
| `_Infra.Actions.Registration.g.cs` | DI registration for all invokers in the assembly |
| `_Boundary.{Name}.Interface.g.cs` | Typed boundary interface (`ICatalogActions`) |

For each `[Mutation]` class, the SG additionally generates:

| Generated Artifact | Purpose |
|-------------------|---------|
| `{Type}.ApplyToEntity.g.cs` | `ApplyToEntity()` with property-to-setter mappings |
| `{Type}.Invoker.g.cs` | Nested `Invoker` class extending `MutationInvoker<T, TEntity>` |

---

## Next Steps

- [Pipeline Guide](pipeline.md) -- understand filter ordering, lifecycle hooks, custom filters
- [Mutations Guide](mutations.md) -- deep dive into entity mutations, auto-mapping, ApplyAsync
- [Boundaries Guide](boundaries.md) -- boundary interfaces, internal calls, remote boundaries
