using Microsoft.CodeAnalysis;
using System.Linq;
using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Jobs.Models;

namespace Pragmatic.SourceGenerator.Features.Jobs.Transforms;

/// <summary>
///     Extracts <see cref="JobModel"/> from [RecurringJob] or [Job] decorated classes.
/// </summary>
internal static class JobTransform
{
    public static JobModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        // Determine if recurring or one-off
        var recurringAttr = symbol.GetAttribute("Pragmatic.Jobs.Attributes.RecurringJobAttribute");
        var jobAttr = symbol.GetAttribute("Pragmatic.Jobs.Attributes.JobAttribute");
        var isRecurring = recurringAttr is not null;

        if (!isRecurring && jobAttr is null)
            return null;

        // Read cron expression and ID from [RecurringJob]
        string? cronExpression = null;
        string? recurringJobId = null;
        string? timeZoneId = null;
        var misfirePolicy = 0;

        if (isRecurring && recurringAttr is not null)
        {
            cronExpression = recurringAttr.ConstructorArguments.Length > 0
                ? recurringAttr.ConstructorArguments[0].Value as string
                : null;
            recurringJobId = recurringAttr.GetNamedArgument<string>("Id");
            timeZoneId = recurringAttr.GetNamedArgument<string>("TimeZone");
            misfirePolicy = IntArgument(recurringAttr, "Misfire", 0);

            // Default ID from class name: DailyReportJob → "daily-report"
            if (string.IsNullOrEmpty(recurringJobId))
                recurringJobId = DeriveJobId(symbol.Name);
        }

        // Every [RecurringJob] on the class, in source order: one job may run on more than one clock.
        // The first may take the id derived from the class name; the ones after it have to name
        // themselves, because they would all derive the same one (PRAG2507, reported by JobsFeature).
        var schedules = ImmutableArray.CreateBuilder<RecurringScheduleModel>();
        var recurringAttrs = symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Jobs.Attributes.RecurringJobAttribute")
            .ToList();

        for (var i = 0; i < recurringAttrs.Count; i++)
        {
            var attr = recurringAttrs[i];
            var cron = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value as string : null;
            if (string.IsNullOrEmpty(cron))
                continue;

            var declaredId = attr.GetNamedArgument<string>("Id");
            schedules.Add(new RecurringScheduleModel
            {
                // An unnamed one after the first keeps the derived id here so the emitted code stays
                // compilable; the diagnostic is what stops the build, not a hole in the output.
                Id = string.IsNullOrEmpty(declaredId) ? DeriveJobId(symbol.Name) : declaredId!,
                CronExpression = cron!,
                TimeZoneId = attr.GetNamedArgument<string>("TimeZone"),
                MisfirePolicy = IntArgument(attr, "Misfire", 0),
            });
        }

        // The count of schedules that rely on the derived id: more than one is the collision.
        var unnamedSchedules = recurringAttrs.Count(a => string.IsNullOrEmpty(a.GetNamedArgument<string>("Id")));

        // Priority / MaxConcurrency live on whichever attribute is present.
        var configAttr = recurringAttr ?? jobAttr;
        var priority = IntArgument(configAttr, "Priority", 0);
        var maxConcurrency = IntArgument(configAttr, "MaxConcurrency", 0);

        // Find IJob or IJob<TParams>
        string? paramTypeFqn = null;
        string? paramTypeShort = null;
        var hasParameters = false;
        var implementsJobInterface = false;

        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface is { IsGenericType: true, Name: "IJob", TypeArguments.Length: 1 })
            {
                var paramType = iface.TypeArguments[0];
                paramTypeFqn = paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                paramTypeShort = paramType.Name;
                hasParameters = true;
                implementsJobInterface = true;
                break;
            }

            if (iface is { IsGenericType: false, Name: "IJob" })
            {
                implementsJobInterface = true;
                break; // parameterless IJob
            }
        }

        // Read [Retry]. The declaration is shared with messaging; the numbers below are this
        // engine's and stay here, because a durable reschedule and a redelivery want different ones.
        var retryAttr = symbol.GetAttribute("Pragmatic.Resilience.Attributes.RetryAttribute");
        var hasRetry = retryAttr is not null;
        // `?? default` only covers a MISSING attribute: GetNamedArgument<int> yields 0 for an
        // argument the user did not set, so a bare [Retry] would get Strategy=Fixed and a zero
        // backoff instead of this engine's defaults. Presence must be tested explicitly —
        // and an explicit 0 must survive, because PRAG2504 reports it as invalid.
        var retryMaxAttempts = IntArgument(retryAttr, "MaxAttempts", 3);
        var retryStrategy = IntArgument(retryAttr, "Strategy", BackoffStrategyValues.Exponential);
        var retryBaseDelayMs = IntArgument(retryAttr, "BaseDelayMs", 1000);

        // Read [Timeout]
        var timeoutAttr = symbol.GetAttribute("Pragmatic.Resilience.Attributes.TimeoutAttribute");
        var hasTimeout = timeoutAttr is not null;
        var timeoutSeconds = IntArgument(timeoutAttr, "TimeoutSeconds", 300);

        // Read [Continuation<T>]
        string? continuationFqn = null;
        var continuationImplementsJob = true;
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null || !attrClass.IsGenericType) continue;
            if (attrClass.OriginalDefinition.ToDisplayString().StartsWith("Pragmatic.Jobs.Attributes.ContinuationAttribute"))
            {
                var typeArg = attrClass.TypeArguments.FirstOrDefault();
                if (typeArg is not null)
                {
                    continuationFqn = typeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    continuationImplementsJob = typeArg is INamedTypeSymbol named
                        && named.AllInterfaces.Any(i => i.Name == "IJob");
                }
                break;
            }
        }

        return new JobModel
        {
            Namespace = symbol.GetNamespaceOrEmpty(),
            TypeName = symbol.Name,
            Accessibility = symbol.GetAccessibilityKeyword(),
            TypeKind = symbol.GetTypeKindKeyword(),
            IsPartial = symbol.IsPartial(),
            IsRecurring = isRecurring,
            CronExpression = cronExpression,
            RecurringJobId = recurringJobId,
            Schedules = schedules.ToImmutable(),
            HasUnnamedExtraSchedule = recurringAttrs.Count > 1 && unnamedSchedules > 1,
            TimeZoneId = timeZoneId,
            HasParameters = hasParameters,
            ParameterTypeFqn = paramTypeFqn,
            ParameterTypeShortName = paramTypeShort,
            HasRetry = hasRetry,
            RetryMaxAttempts = retryMaxAttempts,
            RetryStrategy = retryStrategy,
            RetryBaseDelayMs = retryBaseDelayMs,
            HasTimeout = hasTimeout,
            TimeoutSeconds = timeoutSeconds,
            ContinuationJobTypeFqn = continuationFqn,
            ImplementsJobInterface = implementsJobInterface,
            ContinuationImplementsJob = continuationImplementsJob,
            MisfirePolicy = misfirePolicy,
            Priority = priority,
            MaxConcurrency = maxConcurrency,
            // The same reading a [Service] gets, from the same function: a job is resolved from the
            // container in the same way, and two readings would disagree about what is optional or
            // about who declares [ProvidedByHost].
            Dependencies = Composition.Transforms.ServiceTransform.GetConstructorDependencies(symbol),
            LocationInfo = LocationInfo.From(symbol.Locations.Length > 0 ? symbol.Locations[0] : null),
        };
    }

    // The argument the caller wrote, or this feature's default. Distinguishing absence from an
    // explicit 0 matters: 0 is a value the diagnostics reject, so it must not be silently rewritten
    // into a valid one. Messaging reads the same shared declaration and needs the same rule, so the
    // body lives in shared/SourceGen and this stays as the name the call sites already use.
    private static int IntArgument(AttributeData? attribute, string name, int defaultValue)
        => attribute.GetWrittenIntOrDefault(name, defaultValue);

    private static string DeriveJobId(string className)
    {
        // Strip "Job" suffix, then kebab-case
        var name = className;
        if (name.EndsWith("Job", StringComparison.Ordinal))
            name = name.Substring(0, name.Length - 3);

        return I18nNamingHelper.ToKebabCase(name);
    }
}
