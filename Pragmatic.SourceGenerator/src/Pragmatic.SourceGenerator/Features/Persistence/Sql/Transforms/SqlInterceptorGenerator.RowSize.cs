using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     MySQL's row limit, read off the schema the engine emits for it (PRAG3040).
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    /// <summary>The most a MySQL row holds in its in-row columns, in bytes.</summary>
    private const int MySqlMaxRowBytes = 65535;

    private static readonly Regex SizedType = new(@"^(char|varchar|varbinary)\((\d+)\)$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     <see cref="Diagnostics.MySqlRowTooWide" /> for each table of a MySQL schema whose sized columns add up past the row limit.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read off the types the schema writes, not re-derived from the properties: what the database is asked to create is what is
    ///         counted. utf8mb4, the default and what the measurement used: four bytes a character, plus a length prefix of one byte up to
    ///         255 bytes and two above. <c>longtext</c>, <c>longblob</c> and <c>json</c> are stored off the row and are not counted.
    ///     </para>
    ///     <para>
    ///         A lower bound, which is the honest direction for a warning: the fixed-size columns (the key, the stamps) are not added, so a
    ///         table just under the limit here can still be refused, and one reported here is refused for certain.
    ///     </para>
    /// </remarks>
    internal static void ReportMySqlRowTooWide(SourceProductionContext spc, EquatableArray<EngineSchemaModel> schemas)
    {
        foreach (var schema in schemas.Where(s => s.ProviderName == "MySql"))
        {
            foreach (var table in schema.Tables)
            {
                var sized = table.Columns.Select(c => (c.Name, Bytes: RowBytes(c.SqlType))).Where(c => c.Bytes > 0).ToList();
                var total = sized.Sum(c => c.Bytes);
                if (total <= MySqlMaxRowBytes)
                    continue;

                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MySqlRowTooWide, Location.None,
                    table.TableName, total, string.Join(", ", sized.OrderByDescending(c => c.Bytes).Select(c => $"{c.Name} {c.Bytes}"))));
            }
        }
    }

    /// <summary>The bytes a column of <paramref name="sqlType" /> takes in a MySQL row; 0 for one the row does not hold or that is not sized.</summary>
    internal static int RowBytes(string sqlType)
    {
        var match = SizedType.Match(sqlType);
        if (!match.Success)
            return 0;

        var length = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        return match.Groups[1].Value switch
        {
            "char" => length * 4,
            "varchar" => Prefixed(length * 4),
            _ => Prefixed(length),
        };

        static int Prefixed(int bytes) => bytes + (bytes > 255 ? 2 : 1);
    }
}
