using Pragmatic.Specification.Specifications;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Specification.Tests;

/// <summary>
///     A composed specification's structure is readable: the nodes <c>&amp;</c>, <c>|</c>, <c>!</c> and <c>Spec&lt;T&gt;.True</c>
///     build, and the operands each holds.
/// </summary>
/// <remarks>
///     An engine that compiles each leaf at build time (the Sql engine) composes the compiled leaves along this structure at
///     run time. Read through private fields, the structure was bound to names this assembly never promised.
/// </remarks>
public class NodeTests
{
    [Fact]
    public void ComposedSpecification_ReadsBackItsNodesAndLeaves()
    {
        var active = Spec<User>.Where(u => u.IsActive);
        var admin = Spec<User>.Where(u => u.Role == "Admin");
        var composed = (active & !admin) | Spec<User>.True;

        var or = composed.Should().BeOfType<OrSpecification<User>>().Which;
        or.Right.Should().BeOfType<TrueSpecification<User>>();
        var and = or.Left.Should().BeOfType<AndSpecification<User>>().Which;
        and.Left.Should().BeSameAs(active);
        var not = and.Right.Should().BeOfType<NotSpecification<User>>().Which;
        not.Inner.Should().BeSameAs(admin);
    }

    [Fact]
    public void AndIf_BuildsAnAndWhenItsConditionHolds_AndNothingWhenNot()
    {
        var active = Spec<User>.Where(u => u.IsActive);
        var admin = Spec<User>.Where(u => u.Role == "Admin");

        var and = active.AndIf(true, admin).Should().BeOfType<AndSpecification<User>>().Which;
        and.Left.Should().BeSameAs(active);
        and.Right.Should().BeSameAs(admin);
        active.AndIf(false, admin).Should().BeSameAs(active);
    }
}
