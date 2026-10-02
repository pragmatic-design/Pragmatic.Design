using Pragmatic.FeatureFlags.Samples;

Console.WriteLine("=== Pragmatic.FeatureFlags Samples ===\n");

Console.WriteLine("Sample 1: Basic Feature Flags");
Console.WriteLine("------------------------------");
await BasicFlagsSample.RunAsync();

Console.WriteLine("\nSample 2: Percentage Rollout");
Console.WriteLine("-----------------------------");
await PercentageRolloutSample.RunAsync();

Console.WriteLine("\nSample 3: Targeting Rules");
Console.WriteLine("--------------------------");
await TargetingRulesSample.RunAsync();

Console.WriteLine("\nSample 4: Strongly-Typed Flags");
Console.WriteLine("-------------------------------");
await StronglyTypedFlagsSample.RunAsync();

Console.WriteLine("\nSample 5: Configuration Store + Reload");
Console.WriteLine("---------------------------------------");
await ConfigurationStoreSample.RunAsync();

Console.WriteLine("\nSample 6: Watching for Changes");
Console.WriteLine("-------------------------------");
await WatchChangesSample.RunAsync();

Console.WriteLine("\nSample 7: Ambient Context Provider");
Console.WriteLine("-----------------------------------");
await ContextProviderSample.RunAsync();

Console.WriteLine("\nSample 8: Custom Store Registration");
Console.WriteLine("------------------------------------");
await CustomStoreSample.RunAsync();

Console.WriteLine("\n=== Samples Complete ===");
