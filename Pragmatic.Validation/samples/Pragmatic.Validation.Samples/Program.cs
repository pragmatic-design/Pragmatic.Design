using Pragmatic.Validation.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Validation Samples                     ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ── Core Validation ──────────────────────────────────────────────────────────

// 1. Basic: SG-generated Validate(), Match, ToResult, IHttpError
BasicValidationSample.Run();

// 2. Cross-property: [EqualTo], [RequiredIf], [GreaterThanProperty]
CrossPropertySample.Run();

// 3. Format, date & enum: [Regex], [Guid], [FutureDate], [PastDate], [ValidEnum], [OneOf]
FormatAndDateSample.Run();

// 4. Nested: [ValidateElements], collection bounds, recursive validation
NestedValidationSample.Run();

// ── ValidationError API ──────────────────────────────────────────────────────

// 5. Building, combining, collection expressions, nested paths
ValidationErrorApiSample.Run();

// ── Advanced ─────────────────────────────────────────────────────────────────

// 6. Async validator with real DI: CompositeValidator pipeline
await AsyncValidatorSample.RunAsync();

// 7. Change-aware: validate only modified properties (PATCH support)
ChangeAwareSample.Run();

// 8. Deep nested: object + collection nesting, custom MessageKey
DeepNestedSample.Run();

// 9. IValidatorDecorator: wrap an async validator, resolve bindings against inner type
await ValidatorDecoratorSample.RunAsync();

// 10. ValidationTimeProvider: override the clock for deterministic date validation
TimeProviderSample.Run();

// 11. ValidationDiagnostics: ActivitySource + Meter observability via the pipeline
await DiagnosticsSample.RunAsync();

// 12. DI registration extensions: wire validators by hand (sync/composite/bindings)
await DiServiceRegistrationSample.RunAsync();

// 13. [ValidateElements]: per-element collection validation with indexed paths
ValidateElementsSample.Run();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
