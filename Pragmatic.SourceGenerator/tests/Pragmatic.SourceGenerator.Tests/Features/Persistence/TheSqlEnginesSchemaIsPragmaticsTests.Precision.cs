using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A decimal's precision and scale are <c>[Precision(p, s)]</c> (#731), read by full name on both branches: a decimal with one is that
///     column, one without is (18,2) as before, and EF Core's attribute of the same simple name declares nothing.
/// </summary>
public partial class TheSqlEnginesSchemaIsPragmaticsTests
{
    private const string PrecisionEntity = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Entities;

        [Entity]
        public partial class Tariff : IEntity
        {
            [Precision(18, 4)] public decimal Rate { get; private set; }
            [Precision(12, 0)] public decimal? Units { get; set; }
            public decimal Amount { get; private set; }
            [Microsoft.EntityFrameworkCore.Precision(10, 5)] public decimal Spread { get; private set; }
        }
        """;

    [Theory]
    [InlineData("PostgreSql", "numeric(18,4)", "numeric(12,0)", "numeric(18,2)")]
    [InlineData("SqlServer", "decimal(18,4)", "decimal(12,0)", "decimal(18,2)")]
    [InlineData("Sqlite", "REAL", "REAL", "REAL")]
    public void ADeclaredPrecision_IsTheSameColumnOnBothBranches(string provider, string rate, string units, string undeclared)
    {
        var efCore = Describe(EfCoreSchema(provider, PrecisionEntity)).ToList();
        var (sql, _) = SqlSchema(provider, PrecisionEntity);
        var engine = Describe(sql).ToList();
        foreach (var line in efCore.Where(l => l.Contains("Tariffs.", StringComparison.Ordinal)))
            output.WriteLine(line);

        var tariffs = sql.Tables.Single(t => t.Name == "Tariffs");
        tariffs.Columns.Single(c => c.Name == "Rate").SqlType.Should().Be(rate);
        tariffs.Columns.Single(c => c.Name == "Units").SqlType.Should().Be(units, "a scale of 0 is declared, not absent");
        tariffs.Columns.Single(c => c.Name == "Amount").SqlType.Should().Be(undeclared, "an undeclared decimal stays (18,2)");
        tariffs.Columns.Single(c => c.Name == "Spread").SqlType.Should().Be(undeclared, "EF Core's attribute is not the one read");

        efCore.Except(engine).Concat(engine.Except(efCore)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("MySql", "decimal(18,4)", "decimal(18,2)")]
    [InlineData("Oracle", "NUMBER(18,4)", "NUMBER(18,2)")]
    public void OnMySqlAndOracle_ADeclaredPrecision_IsTheColumn(string dialect, string rate, string undeclared)
    {
        var (schema, _) = SqlSchema(dialect, PrecisionEntity);

        var tariffs = schema.Tables.Single(t => t.Name == "Tariffs");
        tariffs.Columns.Single(c => c.Name == "Rate").SqlType.Should().Be(rate);
        tariffs.Columns.Single(c => c.Name == "Amount").SqlType.Should().Be(undeclared);
    }

    /// <summary>EF Core's configuration carries the precision, whatever the setter; an undeclared decimal gets none from Pragmatic.</summary>
    [Fact]
    public void ADeclaredPrecision_IsInEfCoresConfiguration()
    {
        var (generated, _) = Run([PrecisionEntity, EfCoreGate, Host, ProviderProbe("PostgreSql")], []);
        var configuration = string.Join("\n", generated.SyntaxTrees.Select(t => t.ToString()).Where(s => s.Contains("IEntityTypeConfiguration<", StringComparison.Ordinal)));

        configuration.Should().Contain("e.Rate)").And.Contain("HasPrecision(18, 4)").And.Contain("HasPrecision(12, 0)");
        configuration.Should().NotContain("HasPrecision(10, 5)");
    }
}
