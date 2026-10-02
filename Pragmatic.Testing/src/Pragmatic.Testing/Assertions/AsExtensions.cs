namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Casts inside an assertion chain, where a statement to hold the result would break the flow:
///     <c>events.Should().ContainSingle().Which.As&lt;GuestCheckedIn&gt;().CheckedInBy.Should().Be(…)</c>.
/// </summary>
public static class AsExtensions
{
    /// <summary>
    ///     The subject as <typeparamref name="T"/>, failing with a readable message when it is not.
    /// </summary>
    /// <remarks>
    ///     A plain cast would throw <see cref="InvalidCastException"/> naming two types and nothing
    ///     else. This says which value was being cast, which is what the reader of a failing test
    ///     needs.
    /// </remarks>
    public static T As<T>(this object? subject)
    {
        if (subject is T typed)
            return typed;

        AssertionFailure.Throw(null, $"to be a {typeof(T).Name}",
            subject is null ? "found <null>" : $"found {subject.GetType().Name}");
        return default!;
    }
}
