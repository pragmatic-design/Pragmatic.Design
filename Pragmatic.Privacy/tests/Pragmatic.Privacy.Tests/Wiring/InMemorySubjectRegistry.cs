namespace Pragmatic.Privacy.Tests.Wiring;

/// <summary>
///     A subject registry held in a dictionary, standing in for the EF Core one.
/// </summary>
/// <remarks>
///     The real registry needs an encryptor and a lookup key and is covered by its own suite. What this
///     one has to reproduce is the single property the generated adapters depend on: a subject
///     reference is opaque, and only the registry can turn it back into the identity the rows carry.
///     A fake that returned the reference itself would let an adapter that never called the registry
///     pass.
/// </remarks>
public sealed class InMemorySubjectRegistry : ISubjectRegistry
{
    private readonly Dictionary<string, string> _byReference = new(StringComparer.Ordinal);
    private int _next;

    /// <inheritdoc />
    public ValueTask<string> GetOrCreateReferenceAsync(
        string subjectType, string identifier, CancellationToken ct = default)
    {
        foreach (var (reference, known) in _byReference)
            if (string.Equals(known, identifier, StringComparison.Ordinal))
                return new ValueTask<string>(reference);

        var allocated = $"ref-{++_next:D4}";
        _byReference[allocated] = identifier;
        return new ValueTask<string>(allocated);
    }

    /// <inheritdoc />
    public ValueTask<string?> FindReferenceAsync(
        string subjectType, string identifier, CancellationToken ct = default)
    {
        foreach (var (reference, known) in _byReference)
            if (string.Equals(known, identifier, StringComparison.Ordinal))
                return new ValueTask<string?>(reference);

        return new ValueTask<string?>((string?)null);
    }

    /// <inheritdoc />
    public ValueTask<string?> ResolveIdentityAsync(string subjectRef, CancellationToken ct = default)
        => new(_byReference.TryGetValue(subjectRef, out var identity) ? identity : null);

    /// <inheritdoc />
    public ValueTask<bool> ForgetAsync(string subjectRef, CancellationToken ct = default)
        => new(_byReference.Remove(subjectRef));
}
