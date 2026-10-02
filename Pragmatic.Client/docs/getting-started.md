# Getting Started

This guide walks through creating a typed API client for a Pragmatic-based service.

## Prerequisites

- .NET 10 SDK
- A Pragmatic API server with a `PragmaticManifest.json` (generated automatically by the Pragmatic.Endpoints source generator)

## Step 1: Create the Client Project

```bash
dotnet new classlib -n MyApp.ApiClient
```

## Step 2: Add Package References

In `MyApp.ApiClient.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Pragmatic.Client" />
  <PackageReference Include="Pragmatic.Client.SourceGenerator"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

## Step 3: Provide the Manifest

### Option A: Copy the Manifest File

Copy `PragmaticManifest.json` from the server project output and add it as an additional file:

```xml
<ItemGroup>
  <AdditionalFiles Include="PragmaticManifest.json" />
</ItemGroup>
```

### Option B: Reference the Server Assembly

If both projects are in the same solution, reference the server assembly directly:

```xml
<ProjectReference Include="..\Showcase.Booking\Showcase.Booking.csproj"
                  Private="false"
                  ReferenceOutputAssembly="true" />
```

`Private="false"` ensures the server DLL is only used at compile time and is not copied to the client output.

## Step 4: Build and Inspect

```bash
dotnet build
```

After building, the source generator produces files under `obj/`. For a manifest with `"assembly": "Showcase.Booking"`, you get:

- `IBookingClient.g.cs` -- typed interface
- `BookingHttpClient.g.cs` -- HTTP client implementation
- `CreateGuestRequest.g.cs` -- request DTO
- `GuestDto.g.cs` -- response DTO
- `GuestStatus.g.cs` -- enum
- `ConflictError.g.cs` -- typed error
- `BookingClientExtensions.g.cs` -- DI registration

## Step 5: Register in DI

```csharp
// In your Blazor/MAUI/Console app
builder.Services.AddBookingClient("https://api.example.com");
```

With custom configuration:

```csharp
builder.Services.AddBookingClient("https://api.example.com", client =>
{
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token);
});
```

## Step 6: Inject and Use

```csharp
public class GuestService(IBookingClient client)
{
    public async Task<GuestDto?> GetGuestOrNull(Guid id, CancellationToken ct = default)
    {
        var result = await client.GetGuest(id, ct);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<Guid> CreateGuest(string firstName, string lastName, CancellationToken ct = default)
    {
        var request = new CreateGuestRequest
        {
            FirstName = firstName,
            LastName = lastName
        };

        var result = await client.CreateGuest(request, ct);

        if (result.IsSuccess)
            return result.Value;

        // Typed error matching
        if (result.Error is ConflictError conflict)
            throw new InvalidOperationException($"Guest already exists: {conflict.Code}");

        throw new InvalidOperationException($"API error: {result.Error.Title}");
    }

    public async Task<bool> DeleteGuest(Guid id, CancellationToken ct = default)
    {
        var result = await client.DeleteGuest(id, ct);
        return result.IsSuccess;
    }
}
```

### Calling an endpoint with query parameters

Query parameters become optional arguments after the required ones. Only those you actually pass are
appended to the URL, encoded and formatted with the invariant culture:

```csharp
// GET /api/availability?propertyId=…&checkIn=2026-08-01T00:00:00.0000000%2B00:00&guests=2
var rooms = await client.SearchAvailableRooms(
    propertyId: propertyId,
    checkIn: checkIn,
    checkOut: checkOut,
    guests: 2,
    ct: ct);

// Omitted parameters are simply absent from the query string
var firstPage = await client.SearchGuests(email: "ada@example.com", ct: ct);
```

An endpoint declared as `GET` is issued as a `GET` even when the manifest reports a request body: those
properties (typically `Page` / `PageSize`) travel in the query string, and no request DTO is generated
for it.

## Step 7: Error Handling Patterns

### Pattern matching on typed errors

```csharp
var result = await client.CreateGuest(request);

var message = result.Error switch
{
    ConflictError => "A guest with this email already exists.",
    ApiError { StatusCode: 401 } => "You are not authorized.",
    ApiError { StatusCode: 403 } => "You do not have permission.",
    ApiError api => $"Unexpected error: {api.Title} ({api.Code})",
    _ => "Unknown error"
};
```

### Fallback to ApiError

When the server returns a ProblemDetails response with an unrecognized error code, the client deserializes it into `ApiError`:

```csharp
if (result.Error is ApiError apiError)
{
    Console.WriteLine($"Code: {apiError.Code}");
    Console.WriteLine($"Status: {apiError.StatusCode}");
    Console.WriteLine($"Title: {apiError.Title}");
    Console.WriteLine($"Detail: {apiError.Detail}");

    // Access raw ProblemDetails extensions
    if (apiError.Extensions?.TryGetValue("traceId", out var traceId) == true)
        Console.WriteLine($"Trace: {traceId}");
}
```

## Step 8: Filtering by Boundary (Optional)

If the manifest contains endpoints from multiple boundaries and you only need some:

```xml
<PropertyGroup>
  <PragmaticClientBoundaries>Booking</PragmaticClientBoundaries>
</PropertyGroup>
```

Multiple boundaries separated by `;` or `,`:

```xml
<PragmaticClientBoundaries>Booking;Billing</PragmaticClientBoundaries>
```

## Using with Blazor WebAssembly

```csharp
// Program.cs
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddBookingClient(builder.HostEnvironment.BaseAddress);

await builder.Build().RunAsync();
```

```razor
@* GuestList.razor *@
@inject IBookingClient BookingClient

@code {
    private GuestDto? _guest;

    protected override async Task OnInitializedAsync()
    {
        var result = await BookingClient.GetGuest(GuestId);
        if (result.IsSuccess)
            _guest = result.Value;
    }
}
```

## Using with MAUI

```csharp
// MauiProgram.cs
builder.Services.AddBookingClient("https://api.myapp.com", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
```

## Using with Console / Background Services

```csharp
var services = new ServiceCollection();
services.AddBookingClient("https://api.example.com");

var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IBookingClient>();

var result = await client.GetGuest(Guid.Parse("..."));
```
