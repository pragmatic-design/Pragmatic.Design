using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     PRAG2600 — a trait on a class that is not an entity.
/// </summary>
/// <remarks>
///     The descriptor existed from the start and was never reported: the trait transforms give up by
///     returning no model, so the type vanished before anything could complain about it. Applying
///     <c>[HasComments]</c> to an ordinary class generated nothing and explained nothing.
/// </remarks>
public class TraitRequiresEntityTests
{
    private const string Shims = """
        namespace Pragmatic.Persistence.Entity
        {
            public sealed class EntityAttribute : System.Attribute { }
        }
        namespace Pragmatic.Comments
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class HasCommentsAttribute : System.Attribute
            {
                public int MaxLength { get; set; } = 2000;
            }
        }
        namespace Pragmatic.Tags
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class HasTagsAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public void TraitOnNonEntity_ReportsPrag2600()
    {
        var source = Shims + """

            namespace Sales
            {
                [Pragmatic.Comments.HasComments]
                public partial class NotAnEntity { public string Title { get; set; } = ""; }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        var diagnostics = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG2600").ToList();
        diagnostics.Should().ContainSingle();
        diagnostics[0].GetMessage().Should().Contain("NotAnEntity").And.Contain("HasComments");
    }

    [Fact]
    public void TraitOnEntity_ReportsNothing()
    {
        var source = Shims + """

            namespace Sales
            {
                [Pragmatic.Persistence.Entity.Entity]
                [Pragmatic.Comments.HasComments]
                public partial class Order { public string Title { get; set; } = ""; }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG2600").Should().BeEmpty();
    }

    [Fact]
    public void TraitOnClassDerivedFromEntity_ReportsNothing()
    {
        // [Entity] on a base class is inherited, and the trait transforms resolve the id type
        // through the hierarchy — so the check has to walk it too, or it would fire on valid code.
        var source = Shims + """

            namespace Sales
            {
                [Pragmatic.Persistence.Entity.Entity]
                public partial class BaseOrder { }

                [Pragmatic.Comments.HasComments]
                public partial class SpecialOrder : BaseOrder { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG2600").Should().BeEmpty();
    }

    [Fact]
    public void EveryTraitAttribute_IsChecked()
    {
        var source = Shims + """

            namespace Sales
            {
                [Pragmatic.Tags.HasTags]
                public partial class AlsoNotAnEntity { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        var diagnostics = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG2600").ToList();
        diagnostics.Should().ContainSingle();
        diagnostics[0].GetMessage().Should().Contain("HasTags");
    }

    /// <summary>
    ///     PRAG2600 must accept everything the trait transforms accept, or it fires on working code.
    ///     A type that only implements <c>IEntity</c> has an id the generator can read, so the
    ///     trait is generated — and a diagnostic that fired here would fail the build anyway.
    /// </summary>
    [Fact]
    public void TraitOnTypeImplementingIEntity_ReportsNothing()
    {
        var source = Shims + """

            namespace Pragmatic.Persistence.Entity
            {
                public interface IEntity { System.Guid PersistenceId { get; set; } }
            }
            namespace Sales
            {
                [Pragmatic.Comments.HasComments]
                public partial class Order : Pragmatic.Persistence.Entity.IEntity
                {
                    public System.Guid PersistenceId { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG2600").Should().BeEmpty();
    }

    /// <summary>
    ///     PRAG2650 — the trait's navigation name is already taken. Without it the build fails with a
    ///     CS0102 inside generated code, naming neither the trait nor what to rename.
    /// </summary>
    [Fact]
    public void TraitWhoseNavigationNameIsTaken_ReportsPrag2650()
    {
        var source = Shims + """

            namespace Sales
            {
                [Pragmatic.Persistence.Entity.Entity]
                [Pragmatic.Comments.HasComments]
                public partial class Ticket { public string Comments { get; set; } = ""; }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        var diagnostics = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG2650").ToList();
        diagnostics.Should().ContainSingle();
        diagnostics[0].GetMessage().Should()
            .Contain("Ticket").And.Contain("Comments").And.Contain("HasComments");
    }

    [Fact]
    public void TraitWithoutNavigationCollision_ReportsNoPrag2650()
    {
        var source = Shims + """

            namespace Sales
            {
                [Pragmatic.Persistence.Entity.Entity]
                [Pragmatic.Comments.HasComments]
                public partial class Ticket { public string Title { get; set; } = ""; }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG2650").Should().BeEmpty();
    }
}
