// Sample demonstrating Pragmatic.Endpoints usage
//
// This project showcases ALL endpoint generation features:
//   1. [Endpoint]        → Manual endpoints (HelloEndpoint, GetUserEndpoint, etc.)
//   2. [Autocomplete]    → Autocomplete endpoints from entity properties (Product.Name, Product.Category)
//   3. [Query] + [Endpoint] → Unified query endpoint pipeline (SearchProductsQuery)
//
// Generated endpoints:
//   GET  /api/hello                              → HelloEndpoint
//   GET  /api/limited                            → RateLimitedEndpoint (5 req/min)
//   GET  /api/users/{id}                         → GetUserEndpoint
//   POST /api/users                              → CreateUserEndpoint
//   PUT  /api/users/{id}                         → UpdateUserEndpoint
//   DELETE /api/users/{id}                       → DeleteUserEndpoint
//   GET  /api/users/search                       → SearchUsersEndpoint
//   GET  /api/v2/users                           → ListUsersEndpoint (grouped)
//   POST /api/customers/{customerId}/orders      → CreateOrderEndpoint (DomainAction)
//   POST /api/orders                             → VersionedOrderEndpoint (v1 + v2 via ExecuteV2)
//   GET  /api/products/{id}                      → VersionedProductEndpoint (v1 + v2 via HandleAsyncV2)
//   GET  /api/products/autocomplete/name         → [Autocomplete] on Product.Name
//   GET  /api/products/autocomplete/category     → [Autocomplete] on Product.Category
//   GET  /api/products/search                    → [Query] + [Endpoint] via SearchProductsQuery

using Microsoft.EntityFrameworkCore;
using Pragmatic;
using Pragmatic.Endpoints.Extensions;
using Pragmatic.Endpoints.Samples.Data;
using Pragmatic.Endpoints.Samples.Endpoints;
using Pragmatic.Endpoints.Samples.Services;
using Pragmatic.Persistence.EFCore.Extensions;
using Pragmatic.Persistence.Generated;

var builder = WebApplication.CreateBuilder(args);

// Register application services
builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();

// Register EF Core InMemory for generated endpoints
builder.Services.AddDbContext<SampleDbContext>(options =>
    options.UseInMemoryDatabase("EndpointsSample"));

// Register the runtime options (optional)
builder.Services.AddPragmaticEndpoints(options =>
{
    options.RoutePrefix = "/api";
    options.EnableOpenApi = true;
    options.OpenApiTitle = "Sample API";
});

// Register generated services
builder.Services.AddPragmaticEndpoints();  // Endpoints + inline rate limiters
builder.Services.AddPragmaticActions();    // DomainAction invokers (required for DomainAction endpoints)

// Persistence wiring — two convenience calls cover everything [Entity] + [Query] need:
//   1. AddPragmaticPersistenceEFCore<TDbContext> (runtime)  → IQueryExecutor + DbContext mapping
//   2. AddPragmaticPersistenceRepositories<TDbContext> (SG-generated per assembly) →
//      every {Entity}.Repository + IRepository<T> / IReadRepository<T> + keyed DbContext
// Host-mode apps get the same wiring from PragmaticHost.Services.g.cs; this is the
// library-mode equivalent for minimal APIs / console samples.
builder.Services.AddPragmaticPersistenceEFCore<SampleDbContext>();
builder.Services.AddPragmaticPersistenceRepositories<SampleDbContext>();

var app = builder.Build();

// Ensure database is created with seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    db.Database.EnsureCreated();
}

// Rate limiting middleware (required for [RateLimit] to enforce)
app.UseRateLimiter();

// Map all generated endpoints
app.MapPragmaticEndpoints();

app.Run();
