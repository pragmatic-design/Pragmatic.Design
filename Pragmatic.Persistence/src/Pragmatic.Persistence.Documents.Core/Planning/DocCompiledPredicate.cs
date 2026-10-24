using System.Globalization;
using System.Text;
using System.Text.Json;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Planning;

/// <summary>
///     A condition compiled where it was written (a specification's leaf): its MongoDB conditions written from its values, its Cosmos DB text whose
///     parameters are <c>{p}0</c>, <c>{p}1</c>… (renamed per use, so one leaf can appear twice in a composition), and the writer of value i.
/// </summary>
public sealed class DocCompiledPredicate(DocFilterWriter mongo, string cosmos, int count, Action<Utf8JsonWriter, int, object?[]>? writeValue)
{
    public DocFilterWriter Mongo { get; } = mongo;

    public string Cosmos { get; } = cosmos;

    public int Count { get; } = count;

    public Action<Utf8JsonWriter, int, object?[]>? WriteValue { get; } = writeValue;
}
