using Microsoft.EntityFrameworkCore;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     What EF Core's model makes of an optional <c>[ValueObject]</c>, as the generated configuration maps it (<c>builder.ComplexProperty(e =&gt; e.X)</c>
///     on a nullable property): the reference the migration schema has to match, read from EF Core itself (a provider's conventions, no
///     connection opened) rather than restated.
/// </summary>
public class AnOptionalValueObjectInTheEfModelTests
{
    [Fact]
    public void An_optional_value_objects_members_are_nullable_columns_in_EF_Cores_model()
    {
        using var context = new ExcerptContext();
        var excerpt = context.Model.FindEntityType(typeof(Excerpt))!;

        excerpt.FindComplexProperty(nameof(Excerpt.Highlight))!.IsNullable.Should().BeTrue("the property is nullable: the value object is optional");
        excerpt.FindComplexProperty(nameof(Excerpt.Span))!.IsNullable.Should().BeFalse("the control: a required one is not");
    }

    private sealed record TextSpan(int Start, int Length);

    private sealed class Excerpt
    {
        public Guid Id { get; set; }
        public TextSpan Span { get; set; } = new(0, 0);
        public TextSpan? Highlight { get; set; }
    }

    private sealed class ExcerptContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseNpgsql("Host=unused");

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Excerpt>().ComplexProperty(e => e.Span);
            builder.Entity<Excerpt>().ComplexProperty(e => e.Highlight);
        }
    }
}
