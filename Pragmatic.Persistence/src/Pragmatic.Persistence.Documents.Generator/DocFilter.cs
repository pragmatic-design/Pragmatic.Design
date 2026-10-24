using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pragmatic.Persistence.Documents.Generator;

/// <summary>A value in a condition: C# code that computes it from constants and the chain's slots (<c>s</c>), or BSON null.</summary>
internal sealed class DocValue
{
    public DocValue(string code, bool isNull = false)
    {
        Code = code;
        IsNull = isNull;
    }

    public string Code { get; }

    public bool IsNull { get; }

    public static readonly DocValue Null = new("null", isNull: true);
}

/// <summary>
///     A condition of a document filter, independent of the backend: a comparison, a logical combination, a membership, a text match, an
///     embedded array's element condition. <see cref="Render" /> writes the C# statements that put it into the open filter document for MongoDB
///     (<c>w</c> the writer, <c>s</c> the slots); another backend renders the same tree its own way.
/// </summary>
internal abstract class DocCondition
{
    private const string Values = "global::Pragmatic.Persistence.Documents.DocValues";

    public abstract void Render(StringBuilder source, string indent);

    /// <summary>The condition in Cosmos DB's SQL over <paramref name="root" /> (<c>c</c>, or an EXISTS subquery's element), its values as parameters.</summary>
    public abstract string RenderCosmos(DocCosmosText context, string root);

    /// <summary>The condition as MongoDB's query language, for the PRAG3199 note.</summary>
    public abstract string Describe();

    /// <summary>The conditions of a filter: none (null), one, or several under <c>$and</c>.</summary>
    public static DocCondition? All(List<DocCondition> conditions)
        => conditions.Count == 0 ? null : conditions.Count == 1 ? conditions[0] : new DocLogical("$and", conditions);

    internal sealed class Compare(string path, string op, DocValue value) : DocCondition
    {
        public override void Render(StringBuilder source, string indent)
        {
            var write = value.IsNull ? $"w.WriteNull(\"{op}\");" : $"{Values}.Write(w, \"{op}\", {value.Code});";
            if (path.Length == 0)
            {
                source.AppendLine(indent + write); // an element of an array of values, inside $elemMatch
                return;
            }

            source.AppendLine($"{indent}w.StartDocument({DocTypeEmitter.Literal(path)});");
            source.AppendLine($"{indent}{write}");
            source.AppendLine($"{indent}w.EndDocument();");
        }

        public override string Describe() => path.Length == 0 ? $"{{ {op}: {Show(value)} }}" : $"{{ {path}: {{ {op}: {Show(value)} }} }}";

        public override string RenderCosmos(DocCosmosText context, string root)
        {
            var target = DocCosmosText.Path(path, root);
            if (value.IsNull)
                return op == "$eq" ? $"(NOT IS_DEFINED({target}) OR IS_NULL({target}))" : $"(IS_DEFINED({target}) AND NOT IS_NULL({target}))"; // null or missing, as MongoDB's { $eq: null }
            return $"{target} {DocCosmosText.Operator(op)} {context.Value(value)}";
        }

        /// <summary>The value an equality on <paramref name="field" /> compares with (a partition key's), or null.</summary>
        public DocValue? EqualityOn(string field) => op == "$eq" && path == field && !value.IsNull ? value : null;
    }

    internal sealed class In(string path, DocValue list, bool enumElements) : DocCondition
    {
        public override void Render(StringBuilder source, string indent)
        {
            var values = enumElements ? $"global::System.Linq.Enumerable.Select({list.Code}, static x => (int)x)" : list.Code;
            source.AppendLine($"{indent}w.StartDocument({DocTypeEmitter.Literal(path)});");
            source.AppendLine($"{indent}{Values}.WriteArray(w, \"$in\", {values}, {Values}.Write);");
            source.AppendLine($"{indent}w.EndDocument();");
        }

        public override string Describe() => $"{{ {path}: {{ $in: [list] }} }}";

        public override string RenderCosmos(DocCosmosText context, string root)
        {
            var values = enumElements ? $"global::System.Linq.Enumerable.Select({list.Code}, static x => (int)x)" : list.Code;
            var parameter = context.Parameter($"global::Pragmatic.Persistence.Documents.DocJson.WriteArray(w, {values}, global::Pragmatic.Persistence.Documents.DocJson.WriteValue)");
            return $"ARRAY_CONTAINS({parameter}, {DocCosmosText.Path(path, root)})";
        }
    }

    /// <summary>A text match: Contains, StartsWith, EndsWith, or (both anchors) an equality; <paramref name="ignoreCase" /> for a field's ToLower().</summary>
    internal sealed class Match(string path, DocValue text, bool start, bool end, bool ignoreCase = false) : DocCondition
    {
        public override void Render(StringBuilder source, string indent)
        {
            source.AppendLine($"{indent}w.StartDocument({DocTypeEmitter.Literal(path)});");
            source.AppendLine($"{indent}{Values}.WriteMatch(w, {text.Code}, {(start ? "true" : "false")}, {(end ? "true" : "false")}{(ignoreCase ? ", true" : "")});");
            source.AppendLine($"{indent}w.EndDocument();");
        }

        public override string Describe() => $"{{ {path}: {{ $regex: {(start ? "^" : "")}text{(end ? "$" : "")}{(ignoreCase ? ", $options: i" : "")} }} }}";

        public override string RenderCosmos(DocCosmosText context, string root)
        {
            var parameter = context.Parameter($"global::Pragmatic.Persistence.Documents.DocJson.WriteText(w, {text.Code})");
            var function = start && end ? "STRINGEQUALS" : start ? "STARTSWITH" : end ? "ENDSWITH" : "CONTAINS"; // the text as it is; the case as asked
            return $"{function}({DocCosmosText.Path(path, root)}, {parameter}{(ignoreCase ? ", true" : "")})";
        }
    }

    internal sealed class ElemMatch(string path, DocCondition inner) : DocCondition
    {
        public override void Render(StringBuilder source, string indent)
        {
            source.AppendLine($"{indent}w.StartDocument({DocTypeEmitter.Literal(path)});");
            source.AppendLine($"{indent}w.StartDocument(\"$elemMatch\");");
            inner.Render(source, indent + "    ");
            source.AppendLine($"{indent}w.EndDocument();");
            source.AppendLine($"{indent}w.EndDocument();");
        }

        public override string Describe() => $"{{ {path}: {{ $elemMatch: {inner.Describe()} }} }}";

        public override string RenderCosmos(DocCosmosText context, string root)
        {
            var alias = context.NextAlias();
            return $"EXISTS(SELECT VALUE {alias} FROM {alias} IN {DocCosmosText.Path(path, root)} WHERE {inner.RenderCosmos(context, alias)})";
        }
    }

    /// <summary><c>Any()</c> on an array: it has a first element.</summary>
    internal sealed class NonEmpty(string path) : DocCondition
    {
        public override void Render(StringBuilder source, string indent)
        {
            source.AppendLine($"{indent}w.StartDocument({DocTypeEmitter.Literal(path + ".0")});");
            source.AppendLine($"{indent}w.Write(\"$exists\", true);");
            source.AppendLine($"{indent}w.EndDocument();");
        }

        public override string Describe() => $"{{ {path}.0: {{ $exists: true }} }}";

        public override string RenderCosmos(DocCosmosText context, string root) => $"ARRAY_LENGTH({DocCosmosText.Path(path, root)}) > 0";
    }

    private static string Show(DocValue value) => value.IsNull ? "null" : value.Code.Contains("s[") ? "@arg" : value.Code;
}

/// <summary>$and, $or, $nor over conditions: an array of documents, one per condition.</summary>
internal sealed class DocLogical(string op, List<DocCondition> items) : DocCondition
{
    public string Op { get; } = op;

    public List<DocCondition> Items { get; } = items;

    public override void Render(StringBuilder source, string indent)
    {
        source.AppendLine($"{indent}w.StartArray(\"{Op}\");");
        for (var i = 0; i < Items.Count; i++)
        {
            source.AppendLine($"{indent}w.StartDocument({i});");
            Items[i].Render(source, indent + "    ");
            source.AppendLine($"{indent}w.EndDocument();");
        }

        source.AppendLine($"{indent}w.EndArray();");
    }

    public override string Describe() => $"{{ {Op}: [{string.Join(", ", Items.Select(i => i.Describe()))}] }}";

    public override string RenderCosmos(DocCosmosText context, string root)
    {
        var parts = Items.Select(i => i.RenderCosmos(context, root)).ToList();
        return Op switch
        {
            "$and" => "(" + string.Join(" AND ", parts) + ")",
            "$or" => "(" + string.Join(" OR ", parts) + ")",
            _ => "NOT (" + string.Join(" OR ", parts) + ")", // $nor
        };
    }
}
