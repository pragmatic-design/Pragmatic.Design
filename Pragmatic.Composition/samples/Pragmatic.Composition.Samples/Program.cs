using Pragmatic.Composition.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Composition Samples                    ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// 1. Three-tier model: Topology → Strategy → Business
ThreeTierModelSample.Run();

// 2. IStartupStep: ordering, ConfigureServices, ConfigurePipeline
StartupStepSample.Run();

// 3. SG output: Host.Entry, Host.Services, auto-registration
SgOutputSample.Run();

// 4. Assembly scanning: Scan() fluent API (runnable in-memory DI)
AssemblyScanningSample.Run();

// 5. Service registration attributes: [Service]/[Factory]/[Inject]/[Decorator]
ServiceRegistrationSample.Run();

// 6. Module composition: [IncludeModule]/[Include]/[NeedsStep]/[RequiresConfig]/[UsePackage]
ModuleCompositionSample.Run();

// 7. Options & configuration validation: PragmaticOptions/MaintenanceModeOptions/[RequiresConfig]
OptionsConfigurationSample.Run();

// 8. ControlPlane integration types (runnable: data types + command serialization)
ControlPlaneSample.Run();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
