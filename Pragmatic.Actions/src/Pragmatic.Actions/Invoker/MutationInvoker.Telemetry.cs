using System.Diagnostics;
using Pragmatic.Actions.Diagnostics;
using Pragmatic.Actions.Mutation;
using Pragmatic.Telemetry;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Actions.Invoker;

public abstract partial class MutationInvoker<TMutation, TEntity>
{
    private static void ConfigureMutationActivity(Activity? activity, string mutationType, string entityType, MutationMode mode)
    {
        if (activity is null) return;
        activity.SetTag(ActionTags.Name, mutationType);
        activity.SetTag(ActionTags.Kind, "mutation");
        activity.SetTag(ActionTags.MutationMode, mode.ToString());
        activity.SetTag(ActionTags.EntityType, entityType);
    }

    private static void RecordMutationSuccess(Activity? activity, string mutationType, Stopwatch stopwatch)
    {
        ActionsDiagnostics.MutationDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("mutation.name", mutationType),
            new KeyValuePair<string, object?>("mutation.result", "success"));

        activity?.SetTag(ActionTags.Result, "success");
        activity?.SetSuccess();
    }

    private void RecordMutationException(Activity? activity, string mutationType, Stopwatch stopwatch, Exception ex)
    {
        ActionsDiagnostics.MutationDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("mutation.name", mutationType),
            new KeyValuePair<string, object?>("mutation.result", "exception"));

        ActionsDiagnostics.MutationFailures.Add(1,
            new KeyValuePair<string, object?>("mutation.name", mutationType),
            new KeyValuePair<string, object?>("error.code", ex.GetType().Name));

        activity?.RecordException(ex);
        LogException(mutationType, stopwatch.ElapsedMilliseconds, ex);
    }

    private static void RecordValidationFailure(string mutationType, string level)
    {
        ActionsDiagnostics.MutationValidationFailures.Add(1,
            new KeyValuePair<string, object?>("mutation.name", mutationType),
            new KeyValuePair<string, object?>("validation.level", level));
    }
}
