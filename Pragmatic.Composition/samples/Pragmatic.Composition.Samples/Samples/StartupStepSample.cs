namespace Pragmatic.Composition.Samples.Samples;

/// <summary>
///     IStartupStep: ordering, ConfigureServices, ConfigurePipeline, discovery.
/// </summary>
public static class StartupStepSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("2. IStartupStep — Ordered Service & Pipeline Registration");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  Order ranges:");
        Console.WriteLine("  ───────────────");
        Console.WriteLine("     0- 99: Infrastructure (routing, compression, CORS)");
        Console.WriteLine("    100-499: Module steps (auth, i18n, identity)");
        Console.WriteLine("    500+:    Application/consumer steps");
        Console.WriteLine();

        Console.WriteLine("  Discovery: [StartupStep] attribute on class");
        Console.WriteLine("  ────────────────────────────────────────────────");
        Console.WriteLine("""
            [StartupStep]
            [RequiresConfig("ConnectionStrings:App")]  // Fail-fast validation
            public class BookingStartupStep : IStartupStep
            {
                public int Order => 60;

                public void ConfigureServices(IServiceCollection services,
                    IConfiguration config, IHostEnvironment env)
                {
                    // Register business services
                    services.AddScoped<IBookingService, BookingService>();

                    // Register query filters
                    services.AddScoped<IQueryFilter<Reservation>, TenantReservationFilter>();

                    // Environment-specific
                    if (env.IsDevelopment())
                        services.AddSingleton<IEmailService, FakeEmailService>();
                }

                public void ConfigurePipeline(IApplicationBuilder app)
                {
                    // Middleware ordering matters!
                    app.UseMiddleware<TenantResolutionMiddleware>();
                    app.UseAuthentication();
                    app.UseAuthorization();

                    // OpenAPI documentation
                    app.MapOpenApi();
                    app.MapScalarApiReference();
                }
            }
        """);
        Console.WriteLine();

        Console.WriteLine("  Multiple steps execute in Order:");
        Console.WriteLine("  ──────────────────────────────────");
        Console.WriteLine("    CorsStep(Order=10) → RoutingStep(Order=50) → AppStep(Order=60)");
        Console.WriteLine("    Each step's ConfigureServices runs first, then ConfigurePipeline.");
        Console.WriteLine();
    }
}
