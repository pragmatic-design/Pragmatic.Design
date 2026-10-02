using Pragmatic.Tags.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Pragmatic.Tags — runnable samples.
//
// Pragmatic.Tags is a TRAIT package: the [HasTags] source generator emits the Tag
// and junction entities, their EF configs, the Add/Remove actions, the read side
// and the HTTP endpoints in the consuming project. A console app has no such
// generation pipeline, so the samples below exercise the RUNTIME surface only:
//   1. HasTagsAttribute      — options / defaults
//   2. TagBase / EntityTagBase<TEntityId> — base entity shapes
//   3. ITagPolicy<TEntityId> — default-interface-method hook + custom override
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine("Pragmatic.Tags Samples");
Console.WriteLine("======================");
Console.WriteLine("(The [HasTags] generator runs in a consuming project — see the Showcase.)");
Console.WriteLine();

HasTagsOptionsSample.Run();
TagEntitiesSample.Run();
await TagPolicySample.RunAsync();

Console.WriteLine("All samples completed.");
