using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Expressions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Documents.Templating.Tests;

/// <summary>
///     A zero of any numeric type, and an empty collection, are false in <c>if</c>.
/// </summary>
/// <remarks>
///     Truthiness knew <c>int</c>, <c>long</c> and <c>double</c>, and everything else it had never heard of
///     was true. Amounts are <c>decimal</c>, so <c>if="balance"</c> showed a zero balance; and
///     <c>if="lines"</c> showed the heading of an empty table.
/// </remarks>
public class ZeroAndEmptyAreFalseTests
{
    private readonly ExpressionEvaluator _eval = new();

    public static TheoryData<object> Falsy => new()
    {
        0m,
        0f,
        (short)0,
        (byte)0,
        new List<string>(),
        Array.Empty<int>(),
        Enumerable.Empty<int>().Select(i => i),
    };

    public static TheoryData<object> Truthy => new()
    {
        12.5m,
        0.1f,
        (short)3,
        new List<string> { "a" },
        new[] { 1 },
        Enumerable.Range(1, 1).Select(i => i),
    };

    [Theory]
    [MemberData(nameof(Falsy))]
    public async Task ZeroOrEmpty_IsFalse(object value)
    {
        (await IfAsync(value)).Should().BeFalse();
    }

    /// <summary>The control: the same types holding something are true.</summary>
    [Theory]
    [MemberData(nameof(Truthy))]
    public async Task NonZeroOrNonEmpty_IsTrue(object value)
    {
        (await IfAsync(value)).Should().BeTrue();
    }

    private async Task<bool> IfAsync(object value)
    {
        var context = new TemplateDataContext();
        context.AddSource("value", value);
        return await _eval.EvaluateToBoolAsync(ExpressionParser.ParseExpression("value"), context);
    }
}
