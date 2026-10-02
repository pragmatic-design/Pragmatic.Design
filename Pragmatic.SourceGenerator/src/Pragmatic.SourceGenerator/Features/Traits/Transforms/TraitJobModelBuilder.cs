using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Jobs.Models;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Transforms;

/// <summary>
///     Builds <see cref="JobModel"/> instances for trait-generated background jobs.
/// </summary>
/// <remarks>
///     A generator does not see its own output, so emitting a class annotated with
///     <c>[RecurringJob]</c> is not enough: <c>JobsFeature</c>'s <c>ForAttributeWithMetadataName</c>
///     only scans the user's syntax trees and would never find it — no invoker, no DI registration,
///     no recurring definition. The model is therefore injected into the Jobs pipeline the same way
///     trait actions and endpoints are injected into theirs.
/// </remarks>
internal static class TraitJobModelBuilder
{
    public static JobModel? BuildAttachmentPurgeJob(AttachmentTraitModel model)
    {
        if (!model.PurgeEnabled) return null;

        return new JobModel
        {
            Namespace = model.ParentNamespace,
            TypeName = model.PurgeJobTypeName,
            Accessibility = "public",
            TypeKind = "class",
            IsPartial = true,
            IsRecurring = true,
            CronExpression = model.EffectivePurgeCron,
            RecurringJobId = I18nNamingHelper.ToKebabCase($"Purge{model.ParentTypeName}Attachments"),
            ImplementsJobInterface = true,
            // MisfirePolicy.RunOnce (= 0): a purge missed while the host was down should still happen,
            // once. Skipping it would push the reclaim out by a whole schedule for no benefit.
            MisfirePolicy = 0,
            ContinuationImplementsJob = true,
            LocationInfo = model.LocationInfo,
        };
    }
}
