using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     The context of a Cosmos DB rendering: the parameters (<c>@p0</c>, <c>@p1</c>…) collected as the statements that write them into the
///     query's <c>parameters</c> array (inline: the slots are a span no lambda can hold), the aliases of the nested <c>EXISTS</c> subqueries.
/// </summary>
internal sealed class DocCosmosText(string prefix = "@p")
{
    private const string Json = "global::Pragmatic.Persistence.Documents.DocJson";
    private readonly StringBuilder _parameters = new();
    private int _count;
    private int _aliases;

    /// <summary>The statements that write the parameters (into an open JSON array, <c>w</c> the writer, <c>s</c> the slots).</summary>
    public string Parameters => _parameters.ToString();

    public bool HasParameters => _count > 0;

    /// <summary>Each parameter's value as C# that writes it, in order: a compiled leaf's values, written under names given per use.</summary>
    public List<string> Writes { get; } = [];

    /// <summary>A parameter's name, its value written by <paramref name="write" /> (C# that writes one JSON value with <c>w</c>).</summary>
    public string Parameter(string write)
    {
        Writes.Add(write);
        var name = prefix + _count++; // a family's rule takes its own prefix: its parameters beside the chain's
        _parameters.Append($"w.WriteStartObject(); w.WriteString(\"name\", \"{name}\"); w.WritePropertyName(\"value\"); {write}; w.WriteEndObject(); ");
        return name;
    }

    public string Value(DocValue value) => Parameter($"{Json}.WriteValue(w, {value.Code})");

    public string NextAlias() => "x" + ++_aliases;

    /// <summary>A stored path under its root: <c>c["A"]["B"]</c>; the document's key is <c>id</c>; an empty path is the root itself (an array's value).</summary>
    public static string Path(string path, string root)
    {
        if (path.Length == 0)
            return root;
        var segments = path.Split('.');
        if (root == "c" && segments[0] == "_id")
            segments[0] = "id";
        return root + string.Concat(segments.Select(s => "[" + DocTypeEmitter.Literal(s) + "]"));
    }

    public static string Operator(string op) => op switch
    {
        "$eq" => "=",
        "$ne" => "!=",
        "$lt" => "<",
        "$lte" => "<=",
        "$gt" => ">",
        "$gte" => ">=",
        _ => op,
    };

    /// <summary>
    ///     The whole query: <c>SELECT … FROM c WHERE … ORDER BY …</c>. With <paramref name="families" /> (a Pragmatic entity), the marker the
    ///     runtime replaces with the caller's filter families (<c>DocFamilies</c>), after the chain's conditions or as the WHERE.
    /// </summary>
    public static string Select(string selection, DocCondition? filter, List<(string Path, bool Descending)> sort, DocCosmosText context, string from = "c", bool families = false)
    {
        var text = new StringBuilder("SELECT ").Append(selection).Append(" FROM ").Append(from);
        if (filter is not null)
            text.Append(families ? " WHERE (" : " WHERE ").Append(filter.RenderCosmos(context, "c")).Append(families ? "){families:and}" : "");
        else if (families)
            text.Append("{families:where}");
        if (sort.Count > 0)
            text.Append(" ORDER BY ").Append(string.Join(", ", sort.Select(k => Path(k.Path, "c") + (k.Descending ? " DESC" : " ASC"))));
        return text.ToString();
    }
}
