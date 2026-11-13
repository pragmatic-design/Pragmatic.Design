using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public partial class TheSqlEnginesSchemaIsPragmaticsTests
{
    /// <summary>Strings and binaries declared at MySQL's and Oracle's limits and one past them, each in a table of its own.</summary>
    private const string MySqlAndOracleLengthsEntities = """
        global using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Validation.Attributes;

        namespace Probe.Entities;

        [Entity] public partial class AtMySqlText : IEntity { [MaxLength(16383)] public string Value { get; private set; } = ""; }
        [Entity] public partial class PastMySqlText : IEntity { [MaxLength(16384)] public string Value { get; private set; } = ""; }
        [Entity] public partial class AtOracleText : IEntity { [MaxLength(4000)] public string Value { get; private set; } = ""; }
        [Entity] public partial class PastOracleText : IEntity { [MaxLength(4001)] public string Value { get; private set; } = ""; }
        [Entity] public partial class AtOracleRaw : IEntity { [MaxLength(2000)] public byte[] Value { get; private set; } = []; }
        [Entity] public partial class PastOracleRaw : IEntity { [MaxLength(2001)] public byte[] Value { get; private set; } = []; }
        [Entity] public partial class AtMySqlBinary : IEntity { [MaxLength(65535)] public byte[] Value { get; private set; } = []; }
        [Entity] public partial class PastMySqlBinary : IEntity { [MaxLength(65536)] public byte[] Value { get; private set; } = []; }
        """;

    /// <summary>
    ///     On MySQL and Oracle the Sql engine declares a column the database creates (#712): past the longest <c>varchar</c>/<c>VARCHAR2</c> and
    ///     <c>varbinary</c>/<c>RAW</c>, the large type, and at the limit the length as declared.
    /// </summary>
    /// <remarks>It wrote the declared length whatever it was, which MySQL ("Column length too big") and Oracle (ORA-00910) refuse.</remarks>
    [Theory]
    [InlineData("MySql", "AtMySqlTexts", "varchar(16383)")]
    [InlineData("MySql", "PastMySqlTexts", "longtext")]
    [InlineData("MySql", "AtMySqlBinaries", "varbinary(65535)")]
    [InlineData("MySql", "PastMySqlBinaries", "longblob")]
    [InlineData("Oracle", "AtOracleTexts", "VARCHAR2(4000 CHAR)")]
    [InlineData("Oracle", "PastOracleTexts", "CLOB")]
    [InlineData("Oracle", "AtOracleRaws", "RAW(2000)")]
    [InlineData("Oracle", "PastOracleRaws", "BLOB")]
    public void OnMySqlAndOracle_ALengthPastTheLimit_IsTheLargeType(string dialect, string table, string expected)
    {
        var (schema, _) = SqlSchema(dialect, MySqlAndOracleLengthsEntities);

        schema.Tables.Single(t => t.Name == table).Columns.Single(c => c.Name == "Value").SqlType.Should().Be(expected);
    }

    /// <summary>Two columns each within MySQL's limit and together past its row: the row is reported (PRAG3040), the types stay.</summary>
    private const string WideRowEntity = """
        global using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Validation.Attributes;

        namespace Probe.Entities;

        [Entity]
        public partial class Dossier : IEntity
        {
            [MaxLength(10000)] public string Notes { get; private set; } = "";
            [MaxLength(10000)] public string Remarks { get; private set; } = "";
        }
        """;

    [Fact]
    public void OnMySql_ARowPastTheLimit_IsReported_AndItsColumnsKeepTheirType()
    {
        var (schema, diagnostics) = SqlSchema("MySql", WideRowEntity);

        diagnostics.Should().Contain(d => d.Id == "PRAG3040" && d.GetMessage().Contains("Dossiers")
                                          && d.GetMessage().Contains("Notes 40002") && d.GetMessage().Contains("Remarks 40002"),
            "a varchar(10000) in utf8mb4 takes 40000 bytes and a two-byte prefix, and two of them were refused by MySQL when measured");
        schema.Tables.Single(t => t.Name == "Dossiers").Columns.Single(c => c.Name == "Notes").SqlType.Should().Be("varchar(10000)");
    }

    /// <summary>The control: one such column is within the row, and the same table on PostgreSQL is not MySQL's to report.</summary>
    [Theory]
    [InlineData("MySql", "[MaxLength(10000)] public string Remarks { get; private set; } = \"\";", "")]
    [InlineData("PostgreSql", "", "")]
    public void ARowWithinTheLimit_OrNotOnMySql_IsNotReported(string dialect, string remove, string replacement)
    {
        var entity = remove.Length > 0 ? WideRowEntity.Replace(remove, replacement) : WideRowEntity;

        var (_, diagnostics) = SqlSchema(dialect, entity);

        diagnostics.Should().NotContain(d => d.Id == "PRAG3040");
    }
}
