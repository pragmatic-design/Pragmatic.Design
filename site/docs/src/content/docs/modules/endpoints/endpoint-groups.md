---
title: "Endpoint Groups"
description: "As an API grows, endpoints multiply. You end up with dozens of endpoints sharing the same route prefix (`/api/v1/orders/...`), the same authorization requiremen"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Endpoints/docs/endpoint-groups.md
sidebar:
  order: 5
---
## The Problem

As an API grows, endpoints multiply. You end up with dozens of endpoints sharing the same route prefix (`/api/v1/orders/...`), the same authorization requirements, the same OpenAPI tags, and the same rate limiting policies. Without a grouping mechanism, this shared configuration is repeated on every endpoint, violating DRY and making global changes painful.

Endpoint groups solve this by letting you declare shared configuration once and apply it to a set of related endpoints. Groups define route prefixes, OpenAPI tags, authorization, versioning, and other cross-cutting concerns at the group level.

---

## Declaring a group, and joining it

A group is a class carrying `[EndpointGroup("prefix")]`. An endpoint joins it with `[EndpointGroup<TGroup>]`: the same attribute, with one type argument.

```csharp
[EndpointGroup("/api/v1/orders", Tag = "Orders")]
public sealed class OrdersGroup;

[Endpoint(HttpVerb.Get, "/")]
[EndpointGroup<OrdersGroup>]
public partial class GetOrders : Endpoint<OrderListDto> { }

[Endpoint(HttpVerb.Post, "/")]
[EndpointGroup<OrdersGroup>]
public partial class PlaceOrder : DomainAction<OrderId> { }

[Endpoint(HttpVerb.Get, "/{id}")]
[EndpointGroup<OrdersGroup>]
public partial class GetOrder : Endpoint<OrderDto, NotFoundError>
{
    public required Guid Id { get; init; }
}
```

These three endpoints resolve to:
- `GET /api/v1/orders/`
- `POST /api/v1/orders/`
- `GET /api/v1/orders/{id}`

⚠️ **The group is `sealed`, not `static`.** It is used as a type argument, and a static class cannot be one (CS0718).

Membership has one spelling. There is no `Group = typeof(X)` on `[Endpoint]` and no `Parent = typeof(X)` on the group: both are `[EndpointGroup<TGroup>]`.

### Attribute Properties

| Property | Type | Description |
|----------|------|-------------|
| `RoutePrefix` | `string` | Route prefix prepended to all endpoint routes (constructor parameter) |
| `Tag` | `string?` | OpenAPI tag applied to all endpoints in the group |
| `Version` | `string?` | API version metadata for all endpoints in the group |

### Route Composition

The endpoint's route is relative to the group's `RoutePrefix`. The SG combines them at generation time:

| Group RoutePrefix | Endpoint Route | Final Route |
|-------------------|---------------|-------------|
| `/api/v1/orders` | `/` | `/api/v1/orders/` |
| `/api/v1/orders` | `/{id}` | `/api/v1/orders/{id}` |
| `/api/v1/orders` | `/{id}/items` | `/api/v1/orders/{id}/items` |

If the global `RoutePrefix` is also set in `PragmaticEndpointsOptions`, it is prepended to the group prefix. For example, with `options.RoutePrefix = "/api"` and a group prefix `/v1/orders`, the final route becomes `/api/v1/orders/{endpoint-route}`.

---

## How Groups Work Under the Hood

The SG groups endpoints by their group type during registration. In the generated `MapPragmaticEndpoints()` method, each group with a prefix gets one `MapGroup()` call, and its endpoints are mapped under it:

```csharp
// Group: OrdersGroup
var ordersGroup = root.MapGroup("/api/v1/orders");
// ...the options registered with ConfigureGroup("Orders", …), applied at runtime...

GetOrder.MapEndpoint(ordersGroup);
GetOrders.MapEndpoint(ordersGroup);
PlaceOrder.MapEndpoint(ordersGroup);
```

If the group has a `Version`, the SG adds version metadata to the group:

```csharp
ordersGroup.WithMetadata(new Microsoft.AspNetCore.Mvc.ApiVersionAttribute("2.0"));
```

---

## Nested Groups

A group joins another group the same way an endpoint does: it carries its own `[EndpointGroup("prefix")]` and an `[EndpointGroup<TParent>]`. The route prefixes are concatenated:

```csharp
[EndpointGroup("/api")]
public sealed class ApiGroup;

[EndpointGroup("/v1/orders", Tag = "Orders")]
[EndpointGroup<ApiGroup>]
public sealed class OrdersGroup;

[EndpointGroup("/v1/products", Tag = "Products")]
[EndpointGroup<ApiGroup>]
public sealed class ProductsGroup;

// GET /api/v1/orders/{id}
[Endpoint(HttpVerb.Get, "/{id}")]
[EndpointGroup<OrdersGroup>]
public partial class GetOrder : Endpoint<OrderDto, NotFoundError>
{
    public required Guid Id { get; init; }
}
```

The SG walks up the parent chain to compute the full route prefix, trimming the leading slash of each segment and joining them with `/`:

1. `ApiGroup.RoutePrefix` = `/api`
2. `OrdersGroup.RoutePrefix` = `/v1/orders`
3. Full prefix = `/api/v1/orders`
4. `GetOrder` route = `/{id}`
5. Final route = `/api/v1/orders/{id}`

---

## Programmatic Configuration: `ConfigureGroup`

While `[EndpointGroup]` handles declarative configuration (route prefix, tag, version), `ConfigureGroup()` adds what benefits from runtime control. The SG reads `[EndpointGroup]` at compile time for the route structure, and the generated `MapPragmaticEndpoints()` applies the `ConfigureGroup()` options at runtime: authorization, permissions, rate limiting, tags, output caching and CORS.

```csharp
services.AddPragmaticEndpoints(options =>
{
    options.ConfigureGroup("Orders", group =>
    {
        group.RequireAuthorization = true;
        group.AuthorizationPolicy = "OrdersPolicy";
        group.RateLimitPolicy = "standard";
        group.ResponseCacheDuration = 60;
    });
});
```

**The key is the group class name without its `Group` suffix**: `OrdersGroup` is configured as `"Orders"`, `ProductsApiGroup` as `"ProductsApi"`. A class not ending in `Group` is configured by its simple class name.

**A group can have an empty prefix**, `[EndpointGroup("")]`, when it exists only to share configuration: it still gets its own `MapGroup("")` (route-neutral) and its `ConfigureGroup` options apply to its endpoints like any other group's.

### EndpointGroupOptions Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RoutePrefix` | `string?` | `null` | Route prefix for the group |
| `Tags` | `List<string>` | `[]` | OpenAPI tags |
| `RequireAuthorization` | `bool` | `false` | Whether authorization is required |
| `AuthorizationPolicy` | `string?` | `null` | Named authorization policy |
| `RequiredPermissions` | `List<string>` | `[]` | Permissions required for all endpoints of the group (AND logic). Enforced as a `PragmaticPermissionRequirement` in the group's policy, the same requirement an operation's `[RequirePermission]` emits, so the permissions are in the endpoint's metadata, the 403 names them, and roles and wildcards expand through `IPermissionChecker`. |
| `Version` | `string?` | `null` | API version |
| `RateLimitPolicy` | `string?` | `null` | Named rate limit policy |
| `ResponseCacheDuration` | `int?` | `null` | Default cache duration in seconds |
| `EnableCors` | `bool` | `false` | Enable CORS |
| `CorsPolicy` | `string?` | `null` | Named CORS policy |

### When to Use Programmatic Configuration

The `[EndpointGroup]` attribute is limited to compile-time constants. Programmatic configuration is better for:

- **Authorization policies** that reference dynamic policy names or require complex setup
- **Rate limit policies** that are shared across groups
- **CORS configuration** that varies by environment
- **Cache durations** that differ between development and production

---

## Ungrouped Endpoints

Endpoints without `[EndpointGroup<TGroup>]` are registered directly on the root route builder (with the global `RoutePrefix` if configured). They appear in the generated code under the "Ungrouped endpoints" comment.

---

## Version on Groups

The `Version` property on `[EndpointGroup]` applies API version metadata to all endpoints in the group:

```csharp
[EndpointGroup("/api/v2/products", Tag = "Products", Version = "2.0")]
public sealed class ProductsV2Group;
```

This is metadata only: it attaches `ApiVersionAttribute` metadata to the group's `MapGroup()` route builder. Versioned handlers are a different mechanism (`HandleAsyncV{n}` methods on an endpoint, `ExecuteV{n}` on an action) and need `Asp.Versioning.Http`.

---

## Complete Example

```csharp
// Groups
[EndpointGroup("/api")]
public sealed class ApiGroup;

[EndpointGroup("/v1/orders", Tag = "Orders")]
[EndpointGroup<ApiGroup>]
public sealed class OrdersGroup;

[EndpointGroup("/v1/products", Tag = "Products")]
[EndpointGroup<ApiGroup>]
public sealed class ProductsGroup;

[EndpointGroup("/admin", Tag = "Admin")]
public sealed class AdminGroup;
```

```csharp
// Programmatic configuration
services.AddPragmaticEndpoints(options =>
{
    options.ConfigureGroup("Orders", group =>
    {
        group.RequireAuthorization = true;
        group.RateLimitPolicy = "api-standard";
    });

    options.ConfigureGroup("Admin", group =>
    {
        group.RequireAuthorization = true;
        group.AuthorizationPolicy = "AdminOnly";
        group.RequiredPermissions.Add("admin:access");
    });
});
```

```csharp
// Endpoints join their groups

// GET /api/v1/orders
[Endpoint(HttpVerb.Get, "/")]
[EndpointGroup<OrdersGroup>]
public partial class GetOrders : Endpoint<OrderListDto> { }

// POST /api/v1/orders
[Endpoint(HttpVerb.Post, "/")]
[EndpointGroup<OrdersGroup>]
public partial class PlaceOrder : DomainAction<OrderId> { }

// GET /api/v1/orders/{id}
[Endpoint(HttpVerb.Get, "/{id}")]
[EndpointGroup<OrdersGroup>]
public partial class GetOrder : Endpoint<OrderDto, NotFoundError>
{
    public required Guid Id { get; init; }
}

// GET /api/v1/products
[Endpoint(HttpVerb.Get, "/")]
[EndpointGroup<ProductsGroup>]
public partial class GetProducts : Endpoint<ProductListDto> { }

// GET /admin/dashboard
[Endpoint(HttpVerb.Get, "/dashboard")]
[EndpointGroup<AdminGroup>]
public partial class GetAdminDashboard : Endpoint<DashboardDto> { }
```

The generated `MapPragmaticEndpoints()` creates `MapGroup` calls for `OrdersGroup` (at `/api/v1/orders`) and `ProductsGroup` (at `/api/v1/products`), each with their endpoints registered under the group builder. The `AdminGroup` gets its own `MapGroup` at `/admin`.

---

## Notes

- **Group class is a marker**: the class with `[EndpointGroup]` is a type marker. The SG reads the attribute, not the class body.
- **Route prefix is required**: `RoutePrefix` is a constructor parameter of `[EndpointGroup]`, so it cannot be omitted; an empty one is allowed and still gets its `ConfigureGroup` options (see above).
- **Tags**: a group's `Tag` is the default OpenAPI tag of its endpoints. An endpoint adds its own with `[ApiTags(...)]`.
- **Alphabetical ordering**: within each group, endpoints are sorted by route in the generated registration code, so the output is deterministic across builds.
- **Global prefix stacking**: the global `PragmaticEndpointsOptions.RoutePrefix`, the group prefix and the endpoint route all stack. With `options.RoutePrefix = "/api"`, group prefix `/v1/orders`, and endpoint route `/{id}`, the final route is `/api/v1/orders/{id}`.
