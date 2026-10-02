namespace Pragmatic.Configuration.Kubernetes;

/// <summary>
///     Seam over a named Kubernetes key-value object (a ConfigMap or a Secret): read its whole data map or
///     replace it. Keeps the store logic unit-testable while the real implementations are thin SDK wrappers.
/// </summary>
internal interface IKubernetesBagApi
{
    /// <summary>Reads the object's data (decoded), or <c>null</c> when the object does not exist.</summary>
    Task<IReadOnlyDictionary<string, string>?> ReadAsync(string name, CancellationToken ct);

    /// <summary>Creates or replaces the object with the given data (read-modify-write from the caller).</summary>
    Task UpsertAsync(string name, IReadOnlyDictionary<string, string> data, CancellationToken ct);
}
