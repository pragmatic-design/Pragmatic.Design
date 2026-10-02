namespace Pragmatic.Testing.Mocking;

/// <summary>
///     A mocked property. The generated mock exposes this under the property's own name and
///     implements the interface member explicitly, so the test configures <c>clock.UtcNow.Returns(t)</c>
///     while the system under test reads <c>IClock.UtcNow</c> and goes through <see cref="Get"/>.
/// </summary>
/// <typeparam name="T">The property type.</typeparam>
public sealed class MockProperty<T> : MockMember
{
    private readonly List<T> _assigned = [];
    private Func<T>? _factory;
    private Exception? _throws;

    /// <param name="name">The member's display name, e.g. <c>IClock.UtcNow</c>.</param>
    public MockProperty(string? name = null) : base(name)
    {
    }

    /// <summary>How many times the getter was read by the system under test.</summary>
    public int ReadCount { get; private set; }

    /// <summary>The values the system under test assigned, in order.</summary>
    public IReadOnlyList<T> AssignedValues => _assigned;

    /// <summary>Configures the value the getter returns.</summary>
    public MockProperty<T> Returns(T value)
    {
        _throws = null;
        _factory = () => value;
        return this;
    }

    /// <summary>
    ///     Configures a factory evaluated on every read — for a property whose value changes between
    ///     reads, such as a clock that advances.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
    public MockProperty<T> Returns(Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _throws = null;
        _factory = factory;
        return this;
    }

    /// <summary>Configures the getter to throw.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
    public MockProperty<T> Throws(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _throws = exception;
        return this;
    }

    /// <summary>
    ///     Called by the generated explicit interface implementation. Records the read and returns
    ///     the configured value, or <c>default</c> when nothing was configured.
    /// </summary>
    /// <remarks>
    ///     An unconfigured property returns <c>default</c> rather than throwing, so a test only has to
    ///     configure what it cares about. The cost is that a forgotten <c>Returns</c> surfaces as a
    ///     null or a zero downstream — which is why <see cref="ReadCount"/> exists.
    /// </remarks>
    public T Get()
    {
        ReadCount++;
        if (_throws is not null)
            throw _throws;
        return _factory is null ? default! : _factory();
    }

    /// <summary>
    ///     Called by the generated explicit interface implementation for a settable property.
    ///     Records the assignment and makes it the value subsequent reads return.
    /// </summary>
    public void Set(T value)
    {
        _assigned.Add(value);
        _factory = () => value;
    }

    /// <summary>Asserts the getter was read exactly <paramref name="times"/> times.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void Received(int times = 1) => AssertCallCount(times, ReadCount, ReadCount, null);

    /// <summary>Asserts the getter was never read.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when it was read.</exception>
    public void DidNotReceive() => AssertCallCount(0, ReadCount, ReadCount, null);

    /// <summary>Asserts the setter was assigned exactly <paramref name="times"/> times.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void ReceivedSet(int times = 1) => AssertCallCount(times, _assigned.Count, _assigned.Count, null);

    /// <summary>Asserts the setter was assigned a value matching <paramref name="value"/>.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void ReceivedSet(int times, ArgMatcher<T> value)
    {
        var matching = 0;
        foreach (var assigned in _assigned)
            if (value.Matches(assigned))
                matching++;

        AssertCallCount(times, matching, _assigned.Count, value.ToString());
    }
}
