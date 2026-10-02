namespace Warehouse.Stock.Errors;

/// <summary>
///     A supplier's file refused before any of it was applied: it is not the three columns an import reads,
///     or it has no rows. Nothing was dispatched.
/// </summary>
/// <remarks>
///     The words are in <c>translations/*.json</c>, under <c>error.import.file.unreadable</c>.
/// </remarks>
public sealed partial record ImportFileUnreadableError : Error
{
    public override string Code => "IMPORT_FILE_UNREADABLE";
    public override int StatusCode => 400;

    /// <summary>What is wrong with the file.</summary>
    public string Reason { get; init; } = "";

    public override IReadOnlyDictionary<string, object>? Parameters => new Dictionary<string, object>
    {
        ["reason"] = Reason
    };
}
