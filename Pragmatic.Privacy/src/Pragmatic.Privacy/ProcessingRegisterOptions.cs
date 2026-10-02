namespace Pragmatic.Privacy;

/// <summary>
///     The half of the Article 30 register that cannot be read from the code.
/// </summary>
/// <remarks>
///     Who the controller is, and why each activity happens, are decisions — not facts discoverable in a
///     type. A generator that filled them in would produce a document that reads as authoritative and is
///     not, which is worse than an obviously incomplete one.
/// </remarks>
public sealed class ProcessingRegisterOptions
{
    /// <summary>The controller's name, as it should appear in the register.</summary>
    public string ControllerName { get; set; } = string.Empty;

    /// <summary>How the controller can be reached.</summary>
    public string ControllerContact { get; set; } = string.Empty;

    /// <summary>
    ///     The declared purpose for each entity type, keyed by fully qualified type name.
    /// </summary>
    /// <remarks>
    ///     An entity with no entry here appears in the register with its purpose missing, and is listed
    ///     under <see cref="ProcessingRegister.Incomplete" />. Listing it is the useful behaviour:
    ///     silence would let the gap reach an inspection unnoticed.
    /// </remarks>
    public IDictionary<string, string> Purposes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    ///     The declared purpose for each operation, keyed by fully qualified query or mutation type name.
    /// </summary>
    /// <remarks>
    ///     The dictionary somebody can realistically finish. <see cref="Purposes" /> asks why a type is
    ///     held, which has no single answer when different operations touch it for different reasons;
    ///     this one asks why an operation runs, and each entry has one. Unfilled entries are reported
    ///     through <see cref="ProcessingRegister.IncompleteOperations" />.
    /// </remarks>
    public IDictionary<string, string> OperationPurposes { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
