using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Compositions.Enrichers;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Tests.Core;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Compositions;

/// <summary>
///     Soft-delete cascade targets are found by collection shape, not by a list of type names. A
///     collection shape the detector misses is skipped with no diagnostic, so deleting the parent
///     leaves the child rows alive in the database. These tests pin every shape outside the common
///     BCL names.
/// </summary>
public class SoftDeleteCascadeShapeTests
{
    private const string Source = """
        using System.Collections.Generic;
        using System.Collections.Immutable;

        namespace Pragmatic.Persistence.Entity
        {
            public interface ISoftDelete { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class SoftDeleteAttribute : System.Attribute { public bool Cascade { get; set; } }
        }

        namespace MyApp
        {
            using Pragmatic.Persistence.Entity;

            public sealed class OrderLine : ISoftDelete { }
            public sealed class Payment : ISoftDelete { }
            public sealed class Note : ISoftDelete { }
            public sealed class Tag : ISoftDelete { }
            public sealed class Attachment : ISoftDelete { }
            public sealed class LineBag : List<OrderLine> { }

            // Not soft-deletable: must NOT become a cascade target.
            public sealed class AuditEntry { }

            [SoftDelete(Cascade = true)]
            public sealed class Order : ISoftDelete
            {
                public List<OrderLine> Lines { get; set; } = null!;
                public HashSet<Payment> Payments { get; set; } = null!;
                public Dictionary<string, Note> Notes { get; set; } = null!;
                public ImmutableArray<Tag> Tags { get; set; }
                public Attachment[] Attachments { get; set; } = null!;
                public LineBag Extra { get; set; } = null!;
                public List<AuditEntry> Audit { get; set; } = null!;
                public string Code { get; set; } = "";
            }
        }
        """;

    private static IReadOnlyList<SoftDeleteCascadeTargetModel> CascadeTargets()
    {
        var entity = SymbolCompilationHelper.GetType(Source, "MyApp.Order");
        var contribution = SoftDeleteEnricher.Enrich(entity, MutationModeValue.Delete, explicitSoftDelete: false);

        contribution.Should().NotBeNull();
        contribution!.Cascade.Should().BeTrue();
        return contribution.CascadeTargets.AsImmutableArray();
    }

    [Theory]
    [InlineData("Lines")]       // already worked
    [InlineData("Payments")]    // HashSet<T> — was skipped
    [InlineData("Notes")]       // Dictionary<TKey,TEntity> — was skipped
    [InlineData("Tags")]        // ImmutableArray<T> — was skipped
    [InlineData("Attachments")] // T[] — was skipped
    [InlineData("Extra")]       // custom collection — was skipped
    public void Enrich_SoftDeletableCollectionNavigation_IsACascadeTarget(string propertyName)
    {
        var target = CascadeTargets().SingleOrDefault(t => t.PropertyName == propertyName);

        target.Should().NotBeNull(
            "'{0}' holds soft-deletable children, so the cascade must reach it", propertyName);
        target!.IsCollection.Should().BeTrue();
    }

    [Fact]
    public void Enrich_CollectionOfNonSoftDeletableEntities_IsNotACascadeTarget()
        => CascadeTargets().Should().NotContain(t => t.PropertyName == "Audit");

    [Fact]
    public void Enrich_ScalarProperty_IsNotACascadeTarget()
        => CascadeTargets().Should().NotContain(t => t.PropertyName == "Code");
}
