namespace Pragmatic.Integration.Tests.Infrastructure;

/// <summary>A rate source that answers with how many reads it has served, so a cached answer shows.</summary>
public sealed class CountingRateSource : IRateSource
{
    private int _reads;

    /// <inheritdoc />
    public int Reads => _reads;

    /// <inheritdoc />
    public string Read(string currency) => $"{currency}#{Interlocked.Increment(ref _reads)}";
}
