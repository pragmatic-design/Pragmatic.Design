using Pragmatic.Testing.Assertions;
using Showcase.Billing.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
///     Tests that [ComputedFilter] on Invoice.IsOverdue generates InvoiceComputedFilters
///     with IsOverdueSpec and WhereOverdue extension.
/// </summary>
public class InvoiceComputedFilterTests
{
    [Fact]
    public void InvoiceComputedFilters_ClassExists()
    {
        var type = typeof(Invoice).Assembly
            .GetType("Showcase.Billing.Entities.InvoiceComputedFilters");

        type.Should().NotBeNull("SG should generate InvoiceComputedFilters from [ComputedFilter] on IsOverdue");
    }

    [Fact]
    public void InvoiceComputedFilters_HasIsOverdueSpec()
    {
        var type = typeof(Invoice).Assembly
            .GetType("Showcase.Billing.Entities.InvoiceComputedFilters");

        type.Should().NotBeNull();

        var spec = type!.GetField("IsOverdueSpec");
        spec.Should().NotBeNull("IsOverdueSpec field should exist");
        spec!.FieldType.Name.Should().Contain("Specification");
    }

    [Fact]
    public void InvoiceComputedFilters_HasWhereOverdueExtension()
    {
        var type = typeof(Invoice).Assembly
            .GetType("Showcase.Billing.Entities.InvoiceComputedFilters");

        type.Should().NotBeNull();

        var method = type!.GetMethod("WhereOverdue");
        method.Should().NotBeNull("WhereOverdue extension method should exist");
        method!.IsStatic.Should().BeTrue();
    }
}
