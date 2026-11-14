using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A derived type of a TPH hierarchy has its columns merged into the root's table, and a <c>[MaxLength]</c> declared there is that
///     column's length (#761): the merge carried the precision and not the length, so EF Core's branch said <c>text</c> / <c>nvarchar(max)</c>
///     while the derived type's configuration said <c>HasMaxLength(80)</c>.
/// </summary>
public partial class TheSqlEnginesSchemaIsPragmaticsTests
{
    private const string DerivedLengthEntity = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Entities;

        [Entity]
        [Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "Kind")]
        public partial class Charge : IEntity
        {
            public decimal Amount { get; private set; }
        }

        [Entity]
        public partial class LateCharge : Charge
        {
            [Pragmatic.Validation.Attributes.MaxLength(80)] public string? Reason { get; set; }
        }
        """;

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public void ADerivedTypesLength_IsTheSameColumnOnBothBranches(string provider)
    {
        var efCore = Describe(EfCoreSchema(provider, DerivedLengthEntity)).ToList();
        var (sql, _) = SqlSchema(provider, DerivedLengthEntity);
        var engine = Describe(sql).ToList();
        foreach (var line in efCore.Where(l => l.Contains("Charges.", StringComparison.Ordinal)))
            output.WriteLine(line);

        efCore.Should().Contain(l => l.Contains("column Charges.Reason ", StringComparison.Ordinal) && l.Contains("(80)", StringComparison.Ordinal), "the derived type's length");
        efCore.Except(engine).Concat(engine.Except(efCore)).Should().BeEmpty();
    }
}
