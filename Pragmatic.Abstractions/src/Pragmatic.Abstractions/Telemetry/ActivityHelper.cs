using System.Diagnostics;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Telemetry;

/// <summary>
///     Extension methods for <see cref="Activity" /> following OpenTelemetry conventions.
/// </summary>
public static class ActivityHelper
{
    /// <param name="activity">The activity (may be null if no listener is active).</param>
    extension(Activity? activity)
    {
        /// <summary>
        ///     Records an exception on the activity as an event, following OTel semantic conventions.
        /// </summary>
        /// <param name="ex">The exception to record.</param>
        /// <returns>The activity for chaining.</returns>
        public Activity? RecordException(Exception ex)
        {
            if (activity is null) return null;

            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { ErrorTags.ExceptionType, ex.GetType().FullName },
                { ErrorTags.Message, ex.Message },
                { ErrorTags.Stacktrace, ex.ToString() }
            }));
            return activity;
        }

        /// <summary>
        ///     Sets the activity status to Ok.
        /// </summary>
        /// <returns>The activity for chaining.</returns>
        public Activity? SetSuccess()
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            return activity;
        }

        /// <summary>
        ///     Sets the activity status to Error with an error code.
        /// </summary>
        /// <param name="errorCode">The application error code.</param>
        /// <param name="description">Optional human-readable description.</param>
        /// <returns>The activity for chaining.</returns>
        public Activity? SetFailure(string errorCode, string? description = null)
        {
            if (activity is null) return null;
            activity.SetStatus(ActivityStatusCode.Error, description ?? errorCode);
            activity.SetTag(ErrorTags.Type, errorCode);
            return activity;
        }

        /// <summary>
        ///     Adds a named event to the activity with optional tags.
        /// </summary>
        /// <param name="name">The event name (e.g., "filter.applied", "cache.hit").</param>
        /// <param name="tags">Optional key-value tags for the event.</param>
        /// <returns>The activity for chaining.</returns>
        public Activity? AddNamedEvent(string name,
            params KeyValuePair<string, object?>[] tags)
        {
            if (activity is null) return null;

            if (tags.Length > 0)
                activity.AddEvent(new ActivityEvent(name, tags: new ActivityTagsCollection(tags)));
            else
                activity.AddEvent(new ActivityEvent(name));

            return activity;
        }
    }
}
