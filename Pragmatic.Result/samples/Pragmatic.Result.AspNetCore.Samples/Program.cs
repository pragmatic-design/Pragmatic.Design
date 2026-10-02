using Pragmatic.Result.AspNetCore;
using Pragmatic.Result.AspNetCore.Samples.Services;

var builder = WebApplication.CreateBuilder(args);

// Register Pragmatic.Result services
builder.Services.AddPragmaticResult();

// Register sample services
builder.Services.AddScoped<UserService>();

// Add controllers with global ResultActionFilter
builder.Services.AddControllers(options => { options.Filters.Add<ResultActionFilter>(); });

// Add OpenAPI support (.NET 10 built-in)
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
    // OpenAPI document available at /openapi/v1.json
    app.MapOpenApi();

// =====================================================
// Minimal API with automatic Result handling
// =====================================================
// All endpoints in this group automatically convert Result<T,E> to HTTP responses
var api = app.MapGroup("").WithResultHandling();

// GET user by ID - returns Result<User, NotFoundError> → 200 OK or 404 ProblemDetails
api.MapGet("/api/users/{id}", async (int id, UserService userService)
        => await userService.GetByIdAsync(id))
    .WithName("GetUser");

// POST create user - returns Result<User, ConflictError> → 200 OK or 409 ProblemDetails
api.MapPost("/api/users", async (CreateUserRequest request, UserService userService)
        => await userService.CreateAsync(request))
    .WithName("CreateUser");

// PUT update user - returns Result<User, NotFoundError> → 200 OK or 404 ProblemDetails
api.MapPut("/api/users/{id}", async (int id, UpdateUserRequest request, UserService userService)
        => await userService.UpdateAsync(id, request))
    .WithName("UpdateUser");

// DELETE user - returns VoidResult<NotFoundError> → 204 No Content or 404 ProblemDetails
api.MapDelete("/api/users/{id}", async (int id, UserService userService)
        => await userService.DeleteAsync(id))
    .WithName("DeleteUser");

// =====================================================
// Opt-out example - skip automatic Result handling
// =====================================================
api.MapGet("/api/raw/{id}", [SkipResultHandling] async (int id, UserService userService)
        => await userService.GetByIdAsync(id)) // Returns raw Result<User, NotFoundError>
    .WithName("GetUserRaw");

// Map controllers (also use automatic Result handling via global filter)
app.MapControllers();

Console.WriteLine("Pragmatic.Result.AspNetCore Samples");
Console.WriteLine("====================================");
Console.WriteLine();
Console.WriteLine("Minimal API endpoints (automatic Result handling via ResultActionFilter):");
Console.WriteLine("  GET    /api/users/{id}    - 200 OK or 404 ProblemDetails");
Console.WriteLine("  POST   /api/users         - 200 OK or 409 ProblemDetails");
Console.WriteLine("  PUT    /api/users/{id}    - 200 OK or 404 ProblemDetails");
Console.WriteLine("  DELETE /api/users/{id}    - 204 No Content or 404 ProblemDetails");
Console.WriteLine();
Console.WriteLine("Contra-example — [SkipResultHandling] bypasses the filter:");
Console.WriteLine("  GET    /api/raw/{id}      - ALWAYS 500: JSON serializer reads .Value/.Error on the");
Console.WriteLine("                              raw Result<T,E> and throws. This endpoint exists only");
Console.WriteLine("                              to demonstrate why ResultActionFilter is needed.");
Console.WriteLine();
Console.WriteLine("Controller endpoints (automatic Result handling):");
Console.WriteLine("  GET    /api/controller/users/{id}    - Returns 200 OK or 404 ProblemDetails");
Console.WriteLine("  POST   /api/controller/users         - Returns 201 Created or 409 ProblemDetails");
Console.WriteLine("  PUT    /api/controller/users/{id}    - Returns 204 No Content or 404 ProblemDetails");
Console.WriteLine("  DELETE /api/controller/users/{id}    - Returns 204 No Content or 404 ProblemDetails");
Console.WriteLine();
Console.WriteLine("OpenAPI document:");
Console.WriteLine("  GET    /openapi/v1.json");
Console.WriteLine();
Console.WriteLine("Try it:");
Console.WriteLine("  curl http://localhost:5000/api/users/1      # 200 OK with user");
Console.WriteLine("  curl http://localhost:5000/api/users/999    # 404 ProblemDetails");
Console.WriteLine();

app.Run();
