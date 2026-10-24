using System.Globalization;
using System.Text;
using System.Text.Json;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

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

/// <summary>
///     A condition on <typeparamref name="T" /> composed at runtime from compiled leaves (<c>&amp;</c>, <c>|</c>, <c>!</c>, true): what a chain reads
///     with <c>Matching</c>, a specification as the documents engine runs it. Written per call: MongoDB's <c>$and</c>/<c>$or</c>/<c>$nor</c>, Cosmos
///     DB's text with each leaf's parameters named after its place.
/// </summary>
public sealed class DocPredicate<T>
{
    private readonly char _kind; // L leaf, A and, O or, N not, T true
    private readonly DocCompiledPredicate? _leaf;
    private readonly object?[] _values = [];
    private readonly DocPredicate<T>? _left;
    private readonly DocPredicate<T>? _right;

    private DocPredicate(char kind, DocCompiledPredicate? leaf, object?[]? values, DocPredicate<T>? left, DocPredicate<T>? right)
    {
        _kind = kind;
        _leaf = leaf;
        _values = values ?? [];
        _left = left;
        _right = right;
    }

    public static DocPredicate<T> True { get; } = new('T', null, null, null, null);

    public static DocPredicate<T> Leaf(DocCompiledPredicate predicate, object?[] values) => new('L', predicate, values, null, null);

    public static DocPredicate<T> And(DocPredicate<T> left, DocPredicate<T> right) => new('A', null, null, left, right);

    public static DocPredicate<T> Or(DocPredicate<T> left, DocPredicate<T> right) => new('O', null, null, left, right);

    public static DocPredicate<T> Not(DocPredicate<T> inner) => new('N', null, null, inner, null);

    /// <summary>The condition's elements, into an open filter document.</summary>
    internal void WriteMongo(BsonWriter w)
    {
        switch (_kind)
        {
            case 'L':
                _leaf!.Mongo(w, _values);
                break;
            case 'A' or 'O':
                w.StartArray(_kind == 'A' ? "$and" : "$or");
                w.StartDocument(0);
                _left!.WriteMongo(w);
                w.EndDocument();
                w.StartDocument(1);
                _right!.WriteMongo(w);
                w.EndDocument();
                w.EndArray();
                break;
            case 'N':
                w.StartArray("$nor");
                w.StartDocument(0);
                _left!.WriteMongo(w);
                w.EndDocument();
                w.EndArray();
                break;
            // 'T': no element, every document
        }
    }

    /// <summary>The condition as Cosmos DB's SQL, each leaf's parameters renamed after its place (<c>@__m{n}_{i}</c>) and kept for <see cref="WriteCosmosParameters" />.</summary>
    internal void WriteCosmos(StringBuilder text, List<(DocCompiledPredicate Leaf, object?[] Values, string Prefix)> leaves)
    {
        switch (_kind)
        {
            case 'L':
                var prefix = "@__m" + leaves.Count.ToString(CultureInfo.InvariantCulture) + "_";
                leaves.Add((_leaf!, _values, prefix));
                text.Append('(').Append(_leaf!.Cosmos.Replace("{p}", prefix)).Append(')');
                break;
            case 'A' or 'O':
                text.Append('(');
                _left!.WriteCosmos(text, leaves);
                text.Append(_kind == 'A' ? " AND " : " OR ");
                _right!.WriteCosmos(text, leaves);
                text.Append(')');
                break;
            case 'N':
                text.Append("NOT (");
                _left!.WriteCosmos(text, leaves);
                text.Append(')');
                break;
            default:
                text.Append("true");
                break;
        }
    }

    /// <summary>The parameters of the leaves <see cref="WriteCosmos" /> collected, into the query's open <c>parameters</c> array.</summary>
    internal static void WriteCosmosParameters(Utf8JsonWriter w, List<(DocCompiledPredicate Leaf, object?[] Values, string Prefix)> leaves)
    {
        foreach (var (leaf, values, prefix) in leaves)
        {
            for (var i = 0; i < leaf.Count; i++)
            {
                w.WriteStartObject();
                w.WriteString("name", prefix + i.ToString(CultureInfo.InvariantCulture));
                w.WritePropertyName("value");
                leaf.WriteValue!(w, i, values);
                w.WriteEndObject();
            }
        }
    }
}
