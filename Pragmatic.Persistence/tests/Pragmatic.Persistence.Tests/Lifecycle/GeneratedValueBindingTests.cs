using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.Tests.Lifecycle;

/// <summary>
///     The binding the unit of work applies for a <c>[GeneratedValue]</c> property.
/// </summary>
/// <remarks>
///     Three rules decide whether applying it to every added entry is safe: it writes only into the
///     bound type, only when the value is absent, and it says what it did so the caller can tell
///     "filled" from "left alone".
/// </remarks>
public class GeneratedValueBindingTests
{
    private sealed class Order
    {
        public string Number { get; set; } = "";
    }

    private sealed class Customer
    {
        public string Number { get; set; } = "";
    }

    private sealed class FixedGenerator(string value) : IDefaultValueGenerator<Order, string>
    {
        public int Calls { get; private set; }

        public Task<string> GenerateAsync(Order entity, LifecycleContext context, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(value);
        }
    }

    private static GeneratedValueBinding<Order, string> BindingFor(FixedGenerator generator)
        => new("Number", generator,
            static o => o.Number,
            static (o, v) => o.Number = v,
            static v => string.IsNullOrEmpty(v));

    [Fact]
    public async Task AnEmptyValue_IsFilled()
    {
        var generator = new FixedGenerator("ORD-00001");
        var order = new Order();

        (await BindingFor(generator).TryFillAsync(order, LifecycleContext.At(DateTimeOffset.UnixEpoch), default))
            .Should().BeTrue();

        order.Number.Should().Be("ORD-00001");
    }

    /// <summary>
    ///     A value the caller supplied is kept, and the generator is not even asked.
    /// </summary>
    /// <remarks>
    ///     The call count is the point, not only the resulting string: a sequence-backed generator
    ///     consumes a number every time it runs, so asking and discarding would leave a gap in the
    ///     numbering on every re-save of an existing row.
    /// </remarks>
    [Fact]
    public async Task AValueAlreadySet_IsNeitherOverwrittenNorRegenerated()
    {
        var generator = new FixedGenerator("ORD-00001");
        var order = new Order { Number = "IMPORTED-7" };

        (await BindingFor(generator).TryFillAsync(order, LifecycleContext.At(DateTimeOffset.UnixEpoch), default))
            .Should().BeFalse();

        order.Number.Should().Be("IMPORTED-7");
        generator.Calls.Should().Be(0, "a number not needed is a number not consumed");
    }

    /// <summary>
    ///     The control: another type with the same property shape is left alone.
    /// </summary>
    /// <remarks>
    ///     The unit of work hands every added entry to every binding, so a binding that matched on
    ///     shape rather than on type would write into whatever happened to have a Number.
    /// </remarks>
    [Fact]
    public async Task AnotherEntityType_IsNotTouched()
    {
        var generator = new FixedGenerator("ORD-00001");
        var customer = new Customer();

        (await BindingFor(generator).TryFillAsync(customer, LifecycleContext.At(DateTimeOffset.UnixEpoch), default))
            .Should().BeFalse();

        customer.Number.Should().BeEmpty();
        generator.Calls.Should().Be(0);
    }
}
