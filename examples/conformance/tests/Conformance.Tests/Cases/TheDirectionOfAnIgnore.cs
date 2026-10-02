using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A value the entity computes: read, and not written from outside.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>This does not measure <c>[MapIgnore(MappingDirection.ToEntity)]</c></b>, even though the DTO
///         declares it. The target is a <b>computed</b> property, which has no setter: the write path skips it
///         anyway and says so with <c>PRAG0336</c>, so the case would pass with or without the attribute. The
///         direction is measured where it discriminates — on a writable target — in
///         <c>IgnoreDirectionGeneratorTests</c>.
///     </para>
///     <para>
///         What this measures is real all the same: without <c>PRAG0336</c> a bidirectional DTO exposing a
///         computed value would generate <c>entity.Display = …</c>, that is <b>CS0200 inside a file the author
///         cannot open</b>. The shape is ordinary — a total, a label.
///     </para>
/// </remarks>
public class TheDirectionOfAnIgnore(PostgresFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>
    ///     The read half: it is there, and it is what the entity computes.
    /// </summary>
    [Fact]
    public async Task ItIsStillRead()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = new[] { new { product = "bread", quantity = 3 } },
        }));

        var line = created.GetProperty("lines").EnumerateArray().Single();

        line.GetProperty("display").GetString().Should().Be("bread x3",
            "the ignore applies to the write, so the read stays whole");
    }

    /// <summary>
    ///     ⚠️ And sending it does not write it: the generated code does not attempt the assignment.
    /// </summary>
    /// <remarks>
    ///     The value sent is deliberately different from the one the entity would compute. Without
    ///     <c>PRAG0336</c> this case could not even be written: the project would not compile.
    /// </remarks>
    [Fact]
    public async Task SendingItDoesNotWriteIt()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = new[] { new { product = "bread", quantity = 3, display = "ANYTHING AT ALL" } },
        }));

        var line = created.GetProperty("lines").EnumerateArray().Single();

        line.GetProperty("display").GetString().Should().Be("bread x3",
            "the property is excluded from the write, so what the caller sends does not arrive");
    }
}
