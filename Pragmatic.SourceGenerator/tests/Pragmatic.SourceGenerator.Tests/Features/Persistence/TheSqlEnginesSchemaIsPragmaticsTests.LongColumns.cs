using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public partial class TheSqlEnginesSchemaIsPragmaticsTests
{
    /// <summary>Strings and binaries declared at SQL Server's limits and one past them.</summary>
    private const string LongColumnsEntity = """
        global using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Validation.Attributes;

        namespace Probe.Entities;

        [Entity]
        public partial class Letter : IEntity
        {
            [MaxLength(4000)] public string Summary { get; private set; } = "";
            [MaxLength(5000)] public string Body { get; private set; } = "";
            [MaxLength(8000)] public byte[] Seal { get; private set; } = [];
            [MaxLength(8001)] public byte[] Scan { get; private set; } = [];
        }
        """;

    /// <summary>
    ///     On SQL Server the Sql engine declares a column SQL Server creates: an <c>nvarchar</c> is at most 4000 and a <c>varbinary</c> at most
    ///     8000, and a longer declared length is <c>max</c>, as EF Core's branch writes it (#640).
    /// </summary>
    /// <remarks>
    ///     It wrote <c>nvarchar(5000)</c> and <c>varbinary(8001)</c>, which SQL Server refuses, so the migration of an entity with a long
    ///     <c>[MaxLength]</c> failed there.
    /// </remarks>
    [Theory]
    [InlineData("Summary", "nvarchar(4000)")]
    [InlineData("Body", "nvarchar(max)")]
    [InlineData("Seal", "varbinary(8000)")]
    [InlineData("Scan", "varbinary(max)")]
    public void OnSqlServer_ALengthPastTheLimit_IsMax(string column, string expected)
    {
        var (schema, _) = SqlSchema("SqlServer", LongColumnsEntity);

        schema.Tables.Single(t => t.Name == "Letters").Columns.Single(c => c.Name == column).SqlType.Should().Be(expected);
    }
}
