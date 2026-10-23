using System;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     A predicate translated on its own, outside a plan: a leaf of a predicate the runtime composes (<c>SqlCompiledPredicate</c>). The
///     text of every dialect, on the row alias <c>t</c>, its values as tokens <c>{v:i}</c> in the translator's order.
/// </summary>
internal sealed partial class PlanBuilder
{
    private static readonly SqlDialect[] Dialects = [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    /// <summary>The leaf's text per dialect (SQLite, PostgreSQL, SQL Server, MySQL, Oracle): the dialect pieces resolved, the quoting applied.</summary>
    internal static string[] CompileLeaf(LambdaTranslator translator, string predicate)
    {
        static string Local(string text) => Placeholder.Replace(text, m => "{v:" + m.Groups[1].Value + "}");
        var texts = new string[Dialects.Length];
        for (var d = 0; d < Dialects.Length; d++)
        {
            var sql = Local(predicate);
            for (var pass = 0; pass < 8 && translator.DialectPieces.Count > 0 && sql.IndexOf("{d", StringComparison.Ordinal) >= 0; pass++)
                sql = PieceToken.Replace(sql, m => Local(translator.DialectPieces[int.Parse(m.Groups[1].Value)].For(Dialects[d])));
            texts[d] = ForDialect(sql, Dialects[d]);
        }

        return texts;
    }
}
