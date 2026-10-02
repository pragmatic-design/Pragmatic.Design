using Pragmatic.Testing.Assertions;

using static Pragmatic.Testing.Tests.Assertions.AssertionProbe;

namespace Pragmatic.Testing.Tests.Assertions;

/// <summary>The exception assertions, and the delegate families that produce them.</summary>
public class ExceptionAssertionTests
{
    [Fact]
    public void WithParameterName_ReadsTheArgumentException()
    {
        Action act = () => throw new ArgumentNullException("container");

        act.Should().Throw<ArgumentNullException>().WithParameterName("container");
        Fails(() => act.Should().Throw<ArgumentNullException>().WithParameterName("other"));
    }

    /// <summary>
    ///     An exception that carries no parameter name fails the check rather than passing it —
    ///     asking the question at all means an argument exception was expected.
    /// </summary>
    [Fact]
    public void WithParameterName_OnSomethingElse_Fails()
    {
        Action act = () => throw new InvalidOperationException("no parameter here");

        Fails(() => act.Should().Throw<InvalidOperationException>().WithParameterName("container"));
    }

    [Fact]
    public void Where_ConstrainsTheException_AndChains()
    {
        Action act = () => throw new ArgumentOutOfRangeException("size", 42, "too big");

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Where(e => e.ParamName == "size")
            .And.WithMessage("*too big*");

        Fails(() => act.Should().Throw<ArgumentOutOfRangeException>().Where(e => e.ParamName == "other"));
    }

    [Fact]
    public void ParamName_AndWhich_ExposeTheException()
    {
        Action act = () => throw new ArgumentException("bad", "container");

        act.Should().Throw<ArgumentException>().ParamName.Should().Be("container");
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("bad");
    }

    [Fact]
    public void WithInnerException_RequiresTheInnerType()
    {
        Action act = () => throw new InvalidOperationException("outer", new IOException("inner"));

        act.Should().Throw<InvalidOperationException>()
            .WithInnerException<IOException>().Which.Message.Should().Be("inner");

        Fails(() => act.Should().Throw<InvalidOperationException>().WithInnerException<TimeoutException>());
    }

    [Fact]
    public void WithInnerException_WithNoInnerAtAll_Fails()
    {
        Action act = () => throw new InvalidOperationException("alone");

        Fails(() => act.Should().Throw<InvalidOperationException>().WithInnerException<IOException>());
    }

    /// <summary>
    ///     <c>Throw</c> accepts a subclass; <c>ThrowExactly</c> does not. Without both directions
    ///     asserted, the two are indistinguishable.
    /// </summary>
    [Fact]
    public void ThrowExactly_RejectsASubclass_WhereThrowAcceptsIt()
    {
        Action act = () => throw new ArgumentNullException("x");

        act.Should().Throw<ArgumentException>();
        act.Should().ThrowExactly<ArgumentNullException>();

        Fails(() => act.Should().ThrowExactly<ArgumentException>());
    }

    [Fact]
    public void NotThrow_OfASpecificType_IgnoresOthers()
    {
        Action act = () => throw new InvalidOperationException();

        act.Should().NotThrow<TimeoutException>();
        Fails(() => act.Should().NotThrow<InvalidOperationException>());
    }

    [Fact]
    public async Task AsyncChain_CarriesEveryCheck()
    {
        Func<Task> act = () => Task.FromException(
            new ArgumentNullException("container", "value cannot be null"));

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("container");
        await act.Should().ThrowAsync<ArgumentNullException>().WithMessage("*cannot be null*");
        await act.Should().ThrowAsync<ArgumentNullException>().Where(e => e.ParamName == "container");

        (await act.Should().ThrowAsync<ArgumentNullException>().Subject()).ParamName.Should().Be("container");
    }

    /// <summary>A function hands back what it returned, so the test can go on asserting about it.</summary>
    [Fact]
    public void NotThrow_OnAFunction_ReturnsTheValue()
    {
        Func<string> act = () => "built";

        act.Should().NotThrow().Subject.Should().Be("built");
        act.Should().NotThrow().Which.Should().Be("built");

        Func<string> failing = () => throw new InvalidOperationException();
        Fails(() => failing.Should().NotThrow());
    }

    [Fact]
    public async Task NotThrowAsync_OnAFunction_ReturnsTheValue()
    {
        Func<Task<int>> act = () => Task.FromResult(7);

        (await act.Should().NotThrowAsync()).Subject.Should().Be(7);

        Func<Task<int>> failing = () => Task.FromException<int>(new IOException());
        await failing.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public void ValueTaskDelegates_AreSupported()
    {
        Func<ValueTask> act = () => ValueTask.CompletedTask;
        act.Should().NotThrow();

        Func<ValueTask> failing = () => ValueTask.FromException(new IOException());
        Fails(() => failing.Should().NotThrow());
    }

    [Fact]
    public void Invoking_WrapsACallOnASubject()
    {
        var list = new List<int>();

        list.Invoking(l => l.Add(1)).Should().NotThrow();
        Fails(() => list.Invoking(l => throw new IOException()).Should().NotThrow());

        list.Invoking(l => l.Count).Should().NotThrow();
    }

    [Fact]
    public async Task Awaiting_WrapsAnAsyncCallOnASubject()
    {
        var source = new TaskCompletionSource();
        source.SetResult();

        await source.Awaiting(s => s.Task).Should().NotThrowAsync();
        await source.Awaiting(s => ValueTask.CompletedTask).Should().NotThrowAsync();
        await source.Awaiting(s => Task.FromResult(1)).Should().NotThrowAsync();
        await source.Awaiting(s => ValueTask.FromResult(1)).Should().NotThrowAsync();
    }

    [Fact]
    public void As_CastsInsideAChain_AndSaysWhatItFound()
    {
        object subject = "text";

        subject.As<string>().Should().Be("text");

        var failure = Fails(() => subject.As<Uri>());
        failure.Message.Should().Contain("Uri").And.Contain("String");
    }
}
