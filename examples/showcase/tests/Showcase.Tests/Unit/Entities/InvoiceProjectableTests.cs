using System.Linq.Expressions;
using Pragmatic.Testing.Assertions;
using Showcase.Billing.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
///     Tests that [Projectable] on Invoice.IsOverdue generates expression trees
///     for SQL translation via Invoice.Expr nested class.
/// </summary>
public class InvoiceProjectableTests
{
    [Fact]
    public void InvoiceExpr_NestedClassExists()
    {
        var type = typeof(Invoice).GetNestedType("Expr");

        type.Should().NotBeNull("SG should generate Invoice.Expr nested class from [Projectable]");
    }

    [Fact]
    public void InvoiceExpr_HasIsOverdueExpression()
    {
        var exprType = typeof(Invoice).GetNestedType("Expr");
        exprType.Should().NotBeNull();

        var prop = exprType!.GetProperty("IsOverdue");
        prop.Should().NotBeNull("IsOverdue expression property should exist");
        prop!.PropertyType.Should().BeAssignableTo(typeof(Expression));
    }

    [Fact]
    public void InvoiceExpr_IsOverdue_ReturnsNonNullExpression()
    {
        var exprType = typeof(Invoice).GetNestedType("Expr");
        exprType.Should().NotBeNull();

        var prop = exprType!.GetProperty("IsOverdue");
        var value = prop!.GetValue(null);
        value.Should().NotBeNull("IsOverdue expression should return a non-null expression");
        value.Should().BeAssignableTo<Expression<Func<Invoice, bool>>>();
    }
}
