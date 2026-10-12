using Pragmatic.Logging.Context;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Context;

/// <summary>
///     <c>LogContextScope.PushContext(properties)</c>, the overload the request middleware calls on every request,
///     builds the context's properties at once (#134).
/// </summary>
/// <remarks>
///     It used to call <c>SetProperty</c> once per property, and each call copies the properties into a new
///     array one longer: four properties, four arrays. The pairs are known up front, so they need one.
/// </remarks>
public class PushingPropertiesTogetherAllocatesThemTogetherTests
{
    private static readonly KeyValuePair<string, object?>[] Request =
    [
        new("CorrelationId", "corr-7f3a"),
        new("RequestPath", "/api/orders"),
        new("RequestMethod", "GET"),
        new("RequestId", "req-0042"),
    ];

    [Fact]
    public void PushContextWithProperties_AllocatesLessThanSettingThemOneByOne()
    {
        // Warm both paths, so neither pays a first-call cost the other does not.
        OneByOne().Dispose();
        LogContextScope.PushContext(Request).Dispose();

        var before = GC.GetAllocatedBytesForCurrentThread();
        OneByOne().Dispose();
        var oneByOne = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        LogContextScope.PushContext(Request).Dispose();
        var together = GC.GetAllocatedBytesForCurrentThread() - before;

        together.Should().BeLessThan(oneByOne, "the four pairs are known up front and need one array, not four");
    }

    [Fact]
    public void PushContextWithProperties_ReadsAsSettingThemOneByOne()
    {
        using var context = LogContextScope.PushContext(Request);

        var current = LogContextScope.Current!;
        current.Properties.Keys.Should().Equal("CorrelationId", "RequestPath", "RequestMethod", "RequestId");
        current.GetProperty("RequestId").Should().Be("req-0042");
    }

    [Fact]
    public void PushContextWithProperties_ARepeatedKeyKeepsItsFirstPlaceAndTheLastValue()
    {
        using var context = LogContextScope.PushContext(
        [
            new("A", 1),
            new("B", 2),
            new("A", 3),
        ]);

        var current = LogContextScope.Current!;
        current.Properties.Keys.Should().Equal("A", "B");
        current.GetProperty("A").Should().Be(3);
    }

    [Fact]
    public void PushContextWithProperties_InheritsTheParentAndRestoresItOnDispose()
    {
        using var outer = LogContextScope.PushProperty("TenantId", "acme");
        var parent = LogContextScope.Current;

        using (LogContextScope.PushContext(Request))
        {
            LogContextScope.Current!.Parent.Should().BeSameAs(parent);
            LogContextScope.GetProperty("TenantId").Should().Be("acme");
            LogContextScope.GetProperty("RequestId").Should().Be("req-0042");
        }

        LogContextScope.Current.Should().BeSameAs(parent);
        LogContextScope.GetProperty("RequestId").Should().BeNull();
    }

    private static IDisposable OneByOne()
    {
        var context = LogContextScope.PushContext();
        foreach (var pair in Request)
            LogContextScope.Current!.SetProperty(pair.Key, pair.Value);
        return context;
    }
}
