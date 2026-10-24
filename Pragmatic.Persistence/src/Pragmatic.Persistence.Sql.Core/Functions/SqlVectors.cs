using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql.Functions;

/// <summary>
///     Vectors as the databases exchange them (the JSON text <c>[0.1,0.2,…]</c>: pgvector's and SQL Server's literal form, SQLite's
///     stored text), and the distances SQLite has no function for: register them on the connection as <c>vec_cosine</c>, <c>vec_l2</c>
///     and <c>vec_dot</c> (<c>SqliteConnection.CreateFunction("vec_cosine", SqlVectors.Cosine)</c>) and <c>Sql.CosineDistance</c> and its
///     siblings call them; a full scan, as SQLite has no vector index (an emulation the generator reports).
/// </summary>
public static class SqlVectors
{
    private static readonly Func<System.Data.Common.DbDataReader, int, float[]>?[] s_readers = new Func<System.Data.Common.DbDataReader, int, float[]>?[3];

    /// <summary>
    ///     A provider's own way of reading a vector column (SqlClient hands a <c>SqlVector&lt;float&gt;</c> back, which this assembly does not know):
    ///     the provider package registers it (<c>SqlClientVectors.Register()</c>); without one the column is read as its text.
    /// </summary>
    public static void RegisterReader(SqlDialect dialect, Func<System.Data.Common.DbDataReader, int, float[]> reader) => s_readers[(int)dialect] = reader;

    internal static Func<System.Data.Common.DbDataReader, int, float[]>? ReaderFor(SqlDialect dialect) => s_readers[(int)dialect];

    public static string Format(ReadOnlySpan<float> vector)
    {
        var text = new StringBuilder(vector.Length * 10 + 2).Append('[');
        for (var i = 0; i < vector.Length; i++)
            text.Append(i > 0 ? "," : "").Append(vector[i].ToString("R", CultureInfo.InvariantCulture));
        return text.Append(']').ToString();
    }

    public static float[] Parse(string text)
    {
        var span = text.AsSpan().Trim();
        if (span.Length >= 2 && span[0] == '[' && span[^1] == ']')
            span = span[1..^1];
        if (span.IsWhiteSpace())
            return [];
        var count = 1;
        foreach (var c in span)
        {
            if (c == ',')
                count++;
        }

        var result = new float[count];
        var index = 0;
        foreach (var part in span.Split(','))
            result[index++] = float.Parse(span[part].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return result;
    }

    /// <summary>The cosine distance (1 − cosine similarity) of two vectors in their text form; the SQLite function behind <c>Sql.CosineDistance</c>.</summary>
    public static double Cosine(string a, string b)
    {
        var x = Parse(a);
        var y = Parse(b);
        Check(x, y);
        double dot = 0, nx = 0, ny = 0;
        for (var i = 0; i < x.Length; i++)
        {
            dot += (double)x[i] * y[i];
            nx += (double)x[i] * x[i];
            ny += (double)y[i] * y[i];
        }

        return nx == 0 || ny == 0 ? 1 : 1 - dot / Math.Sqrt(nx * ny);
    }

    /// <summary>The Euclidean distance; the SQLite function behind <c>Sql.L2Distance</c>.</summary>
    public static double L2(string a, string b)
    {
        var x = Parse(a);
        var y = Parse(b);
        Check(x, y);
        double sum = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var d = (double)x[i] - y[i];
            sum += d * d;
        }

        return Math.Sqrt(sum);
    }

    /// <summary>The inner product; the SQLite function behind <c>Sql.InnerProduct</c>.</summary>
    public static double Dot(string a, string b)
    {
        var x = Parse(a);
        var y = Parse(b);
        Check(x, y);
        double sum = 0;
        for (var i = 0; i < x.Length; i++)
            sum += (double)x[i] * y[i];
        return sum;
    }

    private static void Check(float[] x, float[] y)
    {
        if (x.Length != y.Length)
            throw new ArgumentException($"The vectors have {x.Length} and {y.Length} dimensions.");
    }
}
