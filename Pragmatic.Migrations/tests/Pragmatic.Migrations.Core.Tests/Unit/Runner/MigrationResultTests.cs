#pragma warning disable CA2007

using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Runner;

namespace Pragmatic.Migrations.Core.Tests.Unit.Runner;

public class MigrationResultTests
{
    [Fact]
    public void NoChanges_IsSuccess()
    {
        MigrationResult.NoChanges.Success.Should().BeTrue();
        MigrationResult.NoChanges.ChangesApplied.Should().Be(0);
        MigrationResult.NoChanges.Error.Should().BeNull();
    }

    [Fact]
    public void FailedResult_HasSuggestions()
    {
        var result = new MigrationResult(false, 0, TimeSpan.Zero, "SQL", [], "Error")
        {
            FailedChangeIndex = 2,
            FailedChangeSql = "ALTER TABLE ...",
            Suggestions = ["Fix something"]
        };

        result.Success.Should().BeFalse();
        result.FailedChangeIndex.Should().Be(2);
        result.FailedChangeSql.Should().Be("ALTER TABLE ...");
        result.Suggestions.Should().ContainSingle("Fix something");
    }

    [Fact]
    public void SuccessResult_HasNoSuggestions()
    {
        var result = new MigrationResult(true, 5, TimeSpan.FromSeconds(1), null,
            ImmutableArray.Create<SchemaChange>(new DropTable("Test")), null);

        result.Suggestions.Should().BeEmpty();
        result.FailedChangeIndex.Should().BeNull();
    }
}
