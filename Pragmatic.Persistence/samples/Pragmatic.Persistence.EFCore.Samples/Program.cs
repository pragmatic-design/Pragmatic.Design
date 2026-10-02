// Pragmatic.Persistence.EFCore Samples
// Run each sample to see the main persistence features.

using Pragmatic.Persistence.EFCore.Samples.Attachments;
using Pragmatic.Persistence.EFCore.Samples.Cascade;
using Pragmatic.Persistence.EFCore.Samples.DataOwnership;
using Pragmatic.Persistence.EFCore.Samples.DataScopes;
using Pragmatic.Persistence.EFCore.Samples.Filtering;
using Pragmatic.Persistence.EFCore.Samples.Hierarchy;
using Pragmatic.Persistence.EFCore.Samples.Inheritance;
using Pragmatic.Persistence.EFCore.Samples.Lifecycle;
using Pragmatic.Persistence.EFCore.Samples.Lookups;
using Pragmatic.Persistence.EFCore.Samples.Querying;
using Pragmatic.Persistence.EFCore.Samples.Samples;
using Pragmatic.Persistence.EFCore.Samples.StateMachine;
using Pragmatic.Persistence.EFCore.Samples.Temporal;

// ── Core CRUD / query / mutation features ──
await EntityCrudSample.RunAsync();
await PatchSample.RunAsync();
MutationConvertersSample.Run();
SoftDeleteAuditSample.Run();
await BulkOperationsSample.RunAsync();
await QueryExecutorSample.RunAsync();
await ProjectableSample.RunAsync();
await IncludeHintsSample.RunAsync();
await DataOwnershipSample.RunAsync();

// ── Advanced features ──
StateMachineSample.Run();
await LifecycleSample.RunAsync();
await HierarchySample.RunAsync();
await TemporalRelationSample.RunAsync();
await PolymorphicAttachmentSample.RunAsync();
await InheritanceSample.RunAsync();
await CascadeSample.RunAsync();
await GridAndFilterDtoSample.RunAsync();
LookupSample.Run();
FilterToggleSample.Run();
DataScopeRuleSample.Run();

Console.WriteLine("═══ Done ═══");
