// Pragmatic.Internationalization - Samples
// This sample demonstrates the unified internationalization module
// combining globalization (formatting) and localization (translations)

using Pragmatic.Internationalization.Samples.Scenarios;

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║       Pragmatic.Internationalization - Samples               ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Run all samples
TypeSafeCodesSample.Run();
CultureContextSample.Run();
I18NConfigProviderSample.Run();
ValidationHelpersSample.Run();
TestingSupportSample.Run();
MoneyFormattingSample.Run();
LocalizedStringSample.Run();
StronglyTypedKeysSample.Run();
PluralRulesSample.Run();
RelativeTimeSample.Run();
ProviderConfigurationSample.Run();
HumanizerSample.Run();
ValidationAttributesSample.Run();
JsonConvertersSample.Run();
EFCoreConvertersSample.Run();
await AspNetCoreIntegrationSample.RunAsync();

Console.WriteLine();
Console.WriteLine("All samples completed.");
