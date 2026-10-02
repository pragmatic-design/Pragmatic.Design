namespace Pragmatic.Jobs.Tests.Unit;

/// <summary>
///     A registry that knows every job type and declares nothing about any of them.
/// </summary>
/// <remarks>
///     ⚠️ It stands in for the registry in tests that are about something other than the registry,
///     where an empty one will not do. The scheduler refuses a type no registry knows — a row
///     scheduled for one can never run — so a test that schedules with an empty registry is asserting
///     that refusal rather than what it came for. Declaring the knowledge is the honest version: these
///     tests are about what a scheduled row looks like, and in a real application the assembly's
///     generated registry knows its own jobs.
/// </remarks>
internal sealed class KnowsEverything : IJobTypeRegistry
{
    public bool Knows(string jobTypeFqn) => true;

    public object? DeserializeParameters(string jobTypeFqn, string? json) => null;

    public string? SerializeParameters(string jobTypeFqn, object? parameters) => null;

    public Task ExecuteAsync(
        string jobTypeFqn, string? parametersJson, JobContext context,
        IServiceProvider serviceProvider, CancellationToken ct) => Task.CompletedTask;

    public JobRetryPolicy? GetRetryPolicy(string jobTypeFqn) => null;

    public string? GetContinuationJobType(string jobTypeFqn) => null;

    public int GetPriority(string jobTypeFqn) => 0;

    public int GetMaxConcurrency(string jobTypeFqn) => 0;
}
