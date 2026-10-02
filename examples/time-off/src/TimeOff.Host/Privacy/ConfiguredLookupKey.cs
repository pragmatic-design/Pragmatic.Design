using Pragmatic.Privacy;

namespace TimeOff.Host.Privacy;

/// <summary>
///     The key of the blind index the subject registry finds an employee by: a keyed hash of their
///     number, so the table holds nothing that can be searched without it.
/// </summary>
internal sealed class ConfiguredLookupKey(byte[] key) : ISubjectLookupKeyProvider
{
    public ValueTask<byte[]> GetLookupKeyAsync(CancellationToken ct = default) => ValueTask.FromResult(key);
}
