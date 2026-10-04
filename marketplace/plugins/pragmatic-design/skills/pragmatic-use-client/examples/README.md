# Examples: pragmatic-use-client

Copied from `examples/showcase`, which compiles in the repository and is exercised by
`examples/showcase/tests/Showcase.IntegrationTests (GeneratedClientTests)`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`src/Showcase.BlazorClient/Showcase.BlazorClient.csproj`](src/Showcase.BlazorClient/Showcase.BlazorClient.csproj) | Where the manifest comes from: a compile-only reference to the domain assembly (`Private="false"`), the Client generator as an analyzer, and only Client, Result and Abstractions at runtime |
| [`src/Showcase.BlazorClient/Program.cs`](src/Showcase.BlazorClient/Program.cs) | Registering the generated client with `AddBookingClient(baseUrl)` and resolving `IBookingClient` |
| [`tests/Showcase.IntegrationTests/Endpoints/GeneratedClientTests.cs`](tests/Showcase.IntegrationTests/Endpoints/GeneratedClientTests.cs) | Driving the generated client against the running application: a round trip, a query parameter that filters, a GET that stays a GET, an unknown id as a typed error |
