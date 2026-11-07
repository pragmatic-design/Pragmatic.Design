using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A column's length is Pragmatic.Validation's <c>[MaxLength]</c>, the rule and the column in one attribute (#526), read by its full name on
///     both branches: a string column with one is bounded, and DataAnnotations' attribute of the same simple name bounds nothing.
/// </summary>
public partial class TheSqlEnginesSchemaIsPragmaticsTests
{
    private const string LengthEntity = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Entities;

        [Entity]
        public partial class Voucher : IEntity
        {
            [Pragmatic.Validation.Attributes.MaxLength(80)] public string Label { get; private set; } = "";
            [Pragmatic.Validation.Attributes.MaxLength(12)] public string? Code { get; set; }
            [System.ComponentModel.DataAnnotations.MaxLength(40)] public string Note { get; private set; } = "";
        }
        """;

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void ADeclaredLength_IsTheSameColumnOnBothBranches(string provider)
    {
        var efCore = Describe(EfCoreSchema(provider, LengthEntity)).ToList();
        var (sql, _) = SqlSchema(provider, LengthEntity);
        var engine = Describe(sql).ToList();
        foreach (var line in efCore.Where(l => l.Contains("Vouchers.", StringComparison.Ordinal)))
            output.WriteLine(line);

        if (provider != "Sqlite") // SQLite's text has no length
        {
            efCore.Should().Contain(l => l.Contains("column Vouchers.Label ", StringComparison.Ordinal) && l.Contains("(80)", StringComparison.Ordinal), "a private setter's length");
            efCore.Should().Contain(l => l.Contains("column Vouchers.Code ", StringComparison.Ordinal) && l.Contains("(12)", StringComparison.Ordinal), "a public setter's length");
            efCore.Should().NotContain(l => l.Contains("column Vouchers.Note ", StringComparison.Ordinal) && l.Contains("(40)", StringComparison.Ordinal), "DataAnnotations' attribute declares no length");
        }

        efCore.Except(engine).Concat(engine.Except(efCore)).Should().BeEmpty();
    }

    /// <summary>EF Core's configuration bounds the property whatever its setter: the model EF Core validates against is the schema's.</summary>
    [Fact]
    public void ADeclaredLength_IsInEfCoresConfiguration()
    {
        var (generated, _) = Run([LengthEntity, EfCoreGate, Host, ProviderProbe("PostgreSql")], []);
        var configuration = string.Join("\n", generated.SyntaxTrees.Select(t => t.ToString()).Where(s => s.Contains("IEntityTypeConfiguration<", StringComparison.Ordinal)));

        configuration.Should().Contain("e.Label)").And.Contain("HasMaxLength(80)").And.Contain("HasMaxLength(12)");
        configuration.Should().NotContain("HasMaxLength(40)");
    }
}
