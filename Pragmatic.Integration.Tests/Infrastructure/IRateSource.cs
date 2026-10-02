namespace Pragmatic.Integration.Tests.Infrastructure;

/// <summary>Where a rate comes from; counts how many times it was asked.</summary>
public interface IRateSource
{
    /// <summary>How many times <see cref="Read" /> ran.</summary>
    int Reads { get; }

    /// <summary>Reads the rate for a currency.</summary>
    string Read(string currency);
}
