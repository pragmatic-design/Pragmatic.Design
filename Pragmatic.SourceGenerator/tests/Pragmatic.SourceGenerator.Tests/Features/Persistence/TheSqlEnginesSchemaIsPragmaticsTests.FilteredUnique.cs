using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public partial class TheSqlEnginesSchemaIsPragmaticsTests
{
    /// <summary>
    ///     A <c>[Unique]</c> on open rows only (#729): one open stretch per desk, and any number of closed ones; and a soft-deleted entity's
    ///     code unique among its active rows, a boolean in the Sql engine's <c>{bt:1}</c>.
    /// </summary>
    private const string FilteredUniqueEntities = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Entities;

        [Entity]
        [Unique(nameof(Desk), Filter = "\"ValidTo\" IS NULL")]
        public partial class Stretch : IEntity
        {
            public string Desk { get; private set; } = "";
            public DateTimeOffset? ValidTo { get; private set; }
        }

        [Entity]
        [SoftDelete]
        [Unique(nameof(Code), Filter = "\"Active\" = {bt:1}")]
        public partial class Voucher : IEntity
        {
            public string Code { get; private set; } = "";
            public bool Active { get; private set; }
        }
        """;

    /// <summary>
    ///     EF Core's branch gives the database the declared predicate, in the provider's SQL, joined with the live rows' under a soft delete:
    ///     before #729 its <c>[Unique]</c> had no filter, so the index held every row and a closed stretch blocked the next open one.
    /// </summary>
    [Theory]
    [InlineData("PostgreSql", "\"IsDeleted\" = false AND (\"Active\" = TRUE)")]
    [InlineData("SqlServer", "\"IsDeleted\" = 0 AND (\"Active\" = 1)")]
    public void AFilteredUnique_IsAPartialIndexInEfCoresSchema(string provider, string softDeletedFilter)
    {
        var indexes = EfCoreSchema(provider, FilteredUniqueEntities).Tables.SelectMany(t => t.Indexes).ToList();

        indexes.Single(i => i.Name == "IX_Stretches_Desk").Filter.Should().Be("\"ValidTo\" IS NULL");
        indexes.Single(i => i.Name == "IX_Stretches_Desk").IsUnique.Should().BeTrue();
        indexes.Single(i => i.Name == "IX_Vouchers_Code").Filter.Should().Be(softDeletedFilter);
    }

    /// <summary>The Sql engine describes the same partial index, so a database holds one index whichever engine migrated it.</summary>
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public void AFilteredUnique_IsTheSameIndexOnBothBranches(string provider)
    {
        var efCore = Describe(EfCoreSchema(provider, FilteredUniqueEntities)).Where(l => l.Contains(" index ", StringComparison.Ordinal)).ToList();
        var engine = Describe(SqlSchema(provider, FilteredUniqueEntities).Schema).Where(l => l.Contains(" index ", StringComparison.Ordinal)).ToList();

        efCore.Should().Contain(l => l.Contains("IX_Stretches_Desk", StringComparison.Ordinal), "the control: the index is there");
        efCore.Except(engine).Concat(engine.Except(efCore)).Should().BeEmpty();
    }

    /// <summary>
    ///     The model EF Core holds has the same predicate (<c>HasFilter</c>, in the boundary's context, where the provider is known), so the
    ///     model and the database describe one index.
    /// </summary>
    [Fact]
    public void AFilteredUnique_HasItsFilterInTheModel()
    {
        var (generated, _) = Run([FilteredUniqueEntities, EfCoreGate, Host, ProviderProbe("PostgreSql")], []);
        var context = generated.SyntaxTrees.Select(t => t.ToString()).Single(s => s.Contains("class DefaultDbContext", StringComparison.Ordinal)
                                                                                  || s.Contains("DbContext : ", StringComparison.Ordinal) && s.Contains("OnModelCreating", StringComparison.Ordinal));

        context.Should().Contain(".HasIndex(e => e.Desk).IsUnique().HasFilter(\"\\\"ValidTo\\\" IS NULL\");");
    }
}
