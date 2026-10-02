namespace Pragmatic.Specification.Samples.Scenarios;

/// <summary>
///     Contract for runnable samples.
/// </summary>
public interface ISample
{
    string Name { get; }
    string Description { get; }
    void Run();
}