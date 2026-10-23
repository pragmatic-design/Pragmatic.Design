using System;
using System.Collections.Generic;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The catalog's JSON and array entries: a member path into a JSON column, an array's Contains, Count and length, a JSON array of objects
///     tested element by element (Any, Count with a condition), the distinct values of an array of values, a member set, an array column.
/// </summary>
internal static partial class SqlCatalog
{
    private static readonly Entry[] JsonEntries =
    [
        // JSON (a member path into a JSON column: the column, the path as a.b, the member's kind for the cast; an array's Contains and Count; a member set)
        new("JsonValue", 3, JsonValue),
        new("JsonArrayContains", 3, (d, a) => d switch
        {
            SqlDialect.Sqlite => $"EXISTS (SELECT 1 FROM json_each({a[0]}, '$.{a[1]}') WHERE json_each.value = {a[2]})",
            SqlDialect.PostgreSql => $"EXISTS (SELECT 1 FROM jsonb_array_elements_text({a[0]} #> '{{{a[1].Replace('.', ',')}}}') e WHERE e.value = {a[2]})",
            SqlDialect.MySql => $"JSON_CONTAINS({a[0]}, JSON_QUOTE({a[2]}), '$.{a[1]}')", // the candidate as a JSON string: an array of text elements (a number would need JSON_ARRAY semantics)
            SqlDialect.Oracle => $"JSON_EXISTS({a[0]}, '$.{a[1]}[*]?(@ == $v)' PASSING {a[2]} AS \"v\")",
            _ => $"EXISTS (SELECT 1 FROM OPENJSON({a[0]}, '$.{a[1]}') WHERE value = {a[2]})",
        }, isPredicate: true),
        // An array column (SqlArrays): a native array on PostgreSQL, the JSON text of the array elsewhere.
        new("ArrayContains", 2, (d, a) => d switch
        {
            SqlDialect.Sqlite => $"EXISTS (SELECT 1 FROM json_each({a[0]}) WHERE value = {a[1]})",
            SqlDialect.PostgreSql => $"{a[1]} = ANY({a[0]})",
            SqlDialect.MySql => $"JSON_CONTAINS({a[0]}, JSON_ARRAY({a[1]}))", // the candidate as a one-element array: a text stays text, a number a number
            SqlDialect.Oracle => $"JSON_EXISTS({a[0]}, '$[*]?(@ == $v)' PASSING {a[1]} AS \"v\")",
            _ => $"EXISTS (SELECT 1 FROM OPENJSON({a[0]}) WHERE value = {a[1]})",
        }, isPredicate: true),
        // An array column sharing an element with a list of the caller's (x.Tags.Any(t => list.Contains(t))): a[1] the list, expanded per call (an array
        // parameter on PostgreSQL, where && compares two arrays).
        new("ArrayOverlaps", 2, (d, a) => d switch
        {
            SqlDialect.Sqlite => $"EXISTS (SELECT 1 FROM json_each({a[0]}) WHERE value IN ({a[1]}))",
            SqlDialect.PostgreSql => $"({a[0]} && {a[1]})",
            SqlDialect.MySql => $"EXISTS (SELECT /*+ NO_SEMIJOIN() */ 1 FROM JSON_TABLE({a[0]}, '$[*]' COLUMNS (\"value\" VARCHAR(4000) PATH '$')) je WHERE je.\"value\" IN ({a[1]}))",
            SqlDialect.Oracle => $"EXISTS (SELECT 1 FROM JSON_TABLE({a[0]}, '$[*]' COLUMNS (\"value\" VARCHAR2(4000) PATH '$')) je WHERE je.\"value\" IN ({a[1]}))",
            _ => $"EXISTS (SELECT 1 FROM OPENJSON({a[0]}) WHERE value IN ({a[1]}))",
        }, isPredicate: true),
        new("ArrayLength", 1, (d, a) => d switch
        {
            SqlDialect.Sqlite => $"json_array_length({a[0]})",
            SqlDialect.PostgreSql => $"cardinality({a[0]})",
            SqlDialect.MySql => $"JSON_LENGTH({a[0]})",
            SqlDialect.Oracle => $"JSON_VALUE({a[0]}, '$.size()' RETURNING NUMBER)",
            _ => $"(SELECT COUNT(*) FROM OPENJSON({a[0]}))",
        }),
        // An element of a JSON array of objects meeting a condition (a[2], over je."value", the element as JSON): EXISTS over the elements.
        new("JsonArrayAny", 3, (d, a) => d switch
        {
            SqlDialect.Sqlite => $"EXISTS (SELECT 1 FROM json_each({a[0]}, '$.{a[1]}') je WHERE {a[2]})",
            SqlDialect.PostgreSql => $"EXISTS (SELECT 1 FROM jsonb_array_elements({a[0]} #> '{{{a[1].Replace('.', ',')}}}') je(\"value\") WHERE {a[2]})",
            // MySQL 8.4 turns an EXISTS over a correlated JSON_TABLE into a semijoin that materializes the function once (an unconditioned hash join:
            // wrong rows): NO_SEMIJOIN keeps it a per-row subquery.
            SqlDialect.MySql => $"EXISTS (SELECT /*+ NO_SEMIJOIN() */ 1 FROM JSON_TABLE({a[0]}, '$.{a[1]}[*]' COLUMNS (\"value\" JSON PATH '$')) je WHERE {a[2]})",
            SqlDialect.Oracle => $"EXISTS (SELECT 1 FROM JSON_TABLE({a[0]}, '$.{a[1]}[*]' COLUMNS (\"value\" VARCHAR2(4000) FORMAT JSON PATH '$')) je WHERE {a[2]})",
            _ => $"EXISTS (SELECT 1 FROM OPENJSON({a[0]}, '$.{a[1]}') je WHERE {a[2]})",
        }, isPredicate: true),
        new("JsonArrayLength", 2, (d, a) => d switch
        {
            SqlDialect.Sqlite => $"json_array_length({a[0]}, '$.{a[1]}')",
            SqlDialect.PostgreSql => $"jsonb_array_length({a[0]} #> '{{{a[1].Replace('.', ',')}}}')",
            SqlDialect.MySql => $"JSON_LENGTH({a[0]}, '$.{a[1]}')",
            SqlDialect.Oracle => $"JSON_VALUE({a[0]}, '$.{a[1]}.size()' RETURNING NUMBER)",
            _ => $"(SELECT COUNT(*) FROM OPENJSON({a[0]}, '$.{a[1]}'))",
        }),
        new("JsonSet", 3, (d, a) => d switch
        {
            SqlDialect.Sqlite => $"json_set({a[0]}, '$.{a[1]}', {a[2]})",
            SqlDialect.MySql => $"JSON_SET({a[0]}, '$.{a[1]}', {a[2]})",
            SqlDialect.Oracle => $"JSON_TRANSFORM({a[0]}, SET '$.{a[1]}' = {a[2]})",
            SqlDialect.PostgreSql => $"jsonb_set({a[0]}, '{{{a[1].Replace('.', ',')}}}', to_jsonb({a[2]}))",
            _ => $"JSON_MODIFY({a[0]}, '$.{a[1]}', {a[2]})",
        }),
        // A JSON array's elements meeting a condition (a[2], over je."value"), counted; the distinct values of an array of values, counted.
        new("JsonArrayCount", 3, (d, a) => Int(d, d switch
        {
            SqlDialect.Sqlite => $"(SELECT COUNT(*) FROM json_each({a[0]}, '$.{a[1]}') je WHERE {a[2]})",
            SqlDialect.PostgreSql => $"(SELECT COUNT(*) FROM jsonb_array_elements({a[0]} #> '{{{a[1].Replace('.', ',')}}}') je(\"value\") WHERE {a[2]})",
            SqlDialect.MySql => $"(SELECT COUNT(*) FROM JSON_TABLE({a[0]}, '$.{a[1]}[*]' COLUMNS (\"value\" JSON PATH '$')) je WHERE {a[2]})",
            SqlDialect.Oracle => $"(SELECT COUNT(*) FROM JSON_TABLE({a[0]}, '$.{a[1]}[*]' COLUMNS (\"value\" VARCHAR2(4000) FORMAT JSON PATH '$')) je WHERE {a[2]})",
            _ => $"(SELECT COUNT(*) FROM OPENJSON({a[0]}, '$.{a[1]}') je WHERE {a[2]})",
        })),
        new("JsonArrayDistinctCount", 2, (d, a) => Int(d, d switch
        {
            SqlDialect.Sqlite => $"(SELECT COUNT(DISTINCT je.value) FROM json_each({a[0]}, '$.{a[1]}') je)",
            SqlDialect.PostgreSql => $"(SELECT COUNT(DISTINCT je.value) FROM jsonb_array_elements_text({a[0]} #> '{{{a[1].Replace('.', ',')}}}') je(value))",
            SqlDialect.MySql => $"(SELECT COUNT(DISTINCT je.v) FROM JSON_TABLE({a[0]}, '$.{a[1]}[*]' COLUMNS (v VARCHAR(4000) PATH '$')) je)",
            SqlDialect.Oracle => $"(SELECT COUNT(DISTINCT je.v) FROM JSON_TABLE({a[0]}, '$.{a[1]}[*]' COLUMNS (v VARCHAR2(4000) PATH '$')) je)",
            _ => $"(SELECT COUNT(DISTINCT je.value) FROM OPENJSON({a[0]}, '$.{a[1]}') je)",
        })),
    ];
}
