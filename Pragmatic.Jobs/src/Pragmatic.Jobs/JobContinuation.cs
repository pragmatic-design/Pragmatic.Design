using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Serialization;

namespace Pragmatic.Jobs;

/// <summary>
///     Describes a continuation job to enqueue after the current job completes.
/// </summary>
public sealed class JobContinuation
{
    /// <summary>FQN of the next job type.</summary>
    public required string JobType { get; init; }

    /// <summary>Serialized parameters for the next job (JSON).</summary>
    public string? ParametersJson { get; init; }

    /// <summary>Creates a continuation for a parameterless job.</summary>
    public static JobContinuation Then<TJob>() where TJob : IJob
    {
        var typeName = typeof(TJob).FullName
            ?? throw new InvalidOperationException($"Cannot determine the fully-qualified name of {typeof(TJob).Name}. Generic or anonymous types are not supported as job types.");
        return new() { JobType = typeName };
    }

    /// <summary>
    ///     Creates a continuation for a job with parameters, serialized through the host's configured
    ///     JSON seam.
    /// </summary>
    /// <param name="parameters">The parameters to hand to the continuation job.</param>
    /// <param name="jsonOptions">
    ///     The <see cref="PragmaticJsonOptions" /> registered by the host — resolve it from DI and pass
    ///     it here. This is the overload to prefer: contexts registered via <c>UseJson(...)</c>, and a
    ///     disabled reflection fallback, only reach continuation parameters through it.
    /// </param>
    public static JobContinuation Then<TJob, TParams>(TParams parameters, PragmaticJsonOptions jsonOptions)
        where TJob : IJob<TParams>
        where TParams : notnull
    {
        ArgumentNullException.ThrowIfNull(jsonOptions);
        return Create<TJob, TParams>(parameters, jsonOptions);
    }

    /// <summary>Creates a continuation for a job with parameters.</summary>
    /// <remarks>
    ///     ⚠️ This overload has no container to resolve from, so it serializes through
    ///     <see cref="PragmaticJsonOptions.Default" /> — the shared baseline, <b>not</b> the instance the
    ///     host configured with <c>UseJson(...)</c>. Contexts registered by the application do not apply,
    ///     and a <c>DisableReflectionFallback()</c> publish is not honoured here, so an arbitrary
    ///     <typeparamref name="TParams" /> falls back to reflection or fails under Native AOT.
    ///     Prefer <see cref="Then{TJob, TParams}(TParams, PragmaticJsonOptions)" /> wherever the
    ///     configured options can be reached.
    /// </remarks>
    public static JobContinuation Then<TJob, TParams>(TParams parameters)
        where TJob : IJob<TParams>
        where TParams : notnull
        => Create<TJob, TParams>(parameters, PragmaticJsonOptions.Default);

    private static JobContinuation Create<TJob, TParams>(TParams parameters, PragmaticJsonOptions jsonOptions)
        where TJob : IJob<TParams>
        where TParams : notnull
    {
        var typeName = typeof(TJob).FullName
            ?? throw new InvalidOperationException($"Cannot determine the fully-qualified name of {typeof(TJob).Name}. Generic or anonymous types are not supported as job types.");
        return new()
        {
            JobType = typeName,
            // AOT-clean via the JsonTypeInfo-based overload; camelCase matches JobScheduler's output.
            // Full coverage of an arbitrary TParams still depends on the app registering its generated
            // context on the options instance passed in.
            ParametersJson = JsonSerializer.Serialize(
                parameters,
                (JsonTypeInfo<TParams>)jsonOptions.Build().GetTypeInfo(typeof(TParams)))
        };
    }
}
