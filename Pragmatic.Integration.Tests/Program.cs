// Minimal Program.cs required by WebApplicationFactory to discover the test app entry point.
// The actual host configuration is done in IntegrationTestFactory.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
