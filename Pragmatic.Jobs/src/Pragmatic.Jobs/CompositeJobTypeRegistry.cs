namespace Pragmatic.Jobs;

/// <summary>
///     The job types this application can run: every assembly's generated registry, asked in turn.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Why a composite and not one registry.</b> The generator emits one per assembly, and a
///         host has as many as it has assemblies declaring jobs — its modules, and the packages that
///         ship a job of their own. A registration that <c>Replace</c>s whatever is there leaves the
///         last one loaded as the only one that answers: a second module's jobs, or the messaging
///         bridge's <c>PublishMessageJob</c>, would be refused as "Unknown job type".
///     </para>
///     <para>
///         ⚠️ And that refusal arrives late and quietly. For a scheduled message the original has
///         already been acknowledged, so the failure is not a dead letter but a message that exists
///         nowhere.
///     </para>
///     <para>
///         The order is registration order and ties are not broken: two registries claiming one job
///         type would mean two assemblies declaring it, which the type name makes impossible.
///     </para>
/// </remarks>
public sealed class CompositeJobTypeRegistry(IEnumerable<IJobTypeRegistrySource> sources) : IJobTypeRegistry
{
    private readonly IJobTypeRegistrySource[] _sources = sources as IJobTypeRegistrySource[] ?? [.. sources];

    /// <inheritdoc />
    public bool Knows(string jobTypeFqn) => Find(jobTypeFqn) is not null;

    /// <inheritdoc />
    public object? DeserializeParameters(string jobTypeFqn, string? json)
        => Find(jobTypeFqn)?.DeserializeParameters(jobTypeFqn, json);

    /// <inheritdoc />
    public string? SerializeParameters(string jobTypeFqn, object? parameters)
        => Find(jobTypeFqn)?.SerializeParameters(jobTypeFqn, parameters);

    /// <inheritdoc />
    /// <remarks>
    ///     The message names every assembly that was asked: "unknown job type" with nothing else is
    ///     indistinguishable from a host that generated no registry at all, and the two have different
    ///     causes — a missing <c>[Job]</c>, or a package whose registration nobody called.
    /// </remarks>
    public Task ExecuteAsync(
        string jobTypeFqn, string? parametersJson, JobContext context,
        IServiceProvider serviceProvider, CancellationToken ct)
        => Find(jobTypeFqn) is { } source
            ? source.ExecuteAsync(jobTypeFqn, parametersJson, context, serviceProvider, ct)
            : throw new InvalidOperationException(
                $"Unknown job type: {jobTypeFqn}. {Asked()}");

    /// <inheritdoc />
    public JobRetryPolicy? GetRetryPolicy(string jobTypeFqn) => Find(jobTypeFqn)?.GetRetryPolicy(jobTypeFqn);

    /// <inheritdoc />
    public string? GetContinuationJobType(string jobTypeFqn) => Find(jobTypeFqn)?.GetContinuationJobType(jobTypeFqn);

    /// <inheritdoc />
    public int GetPriority(string jobTypeFqn) => Find(jobTypeFqn)?.GetPriority(jobTypeFqn) ?? 0;

    /// <inheritdoc />
    public int GetMaxConcurrency(string jobTypeFqn) => Find(jobTypeFqn)?.GetMaxConcurrency(jobTypeFqn) ?? 0;

    private IJobTypeRegistrySource? Find(string jobTypeFqn)
    {
        foreach (var source in _sources)
        {
            if (source.Knows(jobTypeFqn))
                return source;
        }

        return null;
    }

    private string Asked()
        => _sources.Length == 0
            ? "No registry is registered: no assembly in this application declares a [Job], or the "
              + "package that ships one registers its own and was not asked to."
            : $"Asked {_sources.Length} registr{(_sources.Length == 1 ? "y" : "ies")}.";
}
