using System.Runtime.CompilerServices;

namespace Pragmatic.Testing.Assertions;

/// <summary>Assertions about a <see cref="Type"/> itself — its base, and what it implements.</summary>
public sealed class TypeAssertions : SubjectAssertions<Type?, TypeAssertions>
{
    internal TypeAssertions(Type? subject, string? expression) : base(subject, expression) { }

    /// <summary>Fails unless the subject is <typeparamref name="TExpected"/>.</summary>
    public AndConstraint<TypeAssertions> Be<TExpected>(string? because = null, params object[] becauseArgs) =>
        Be(typeof(TExpected), because, becauseArgs);

    /// <summary>Fails unless the type the subject represents can be assigned to <typeparamref name="T"/>.</summary>
    /// <remarks>
    ///     Hides the inherited member, which asks whether the <see cref="Type"/> <b>object</b> is a
    ///     <typeparamref name="T"/> — it never is, and the failure read "found RuntimeType". Here the
    ///     question is about the type the object describes, which is the only reading that makes
    ///     sense of <c>typeof(NoteBase&lt;Guid&gt;).Should().BeAssignableTo&lt;ISoftDelete&gt;()</c>.
    /// </remarks>
    public new AndConstraint<TypeAssertions> BeAssignableTo<T>(string? because = null, params object[] becauseArgs) =>
        BeAssignableTo(typeof(T), because, becauseArgs);

    /// <summary>Fails unless the type the subject represents can be assigned to <paramref name="expected"/>.</summary>
    public new AndConstraint<TypeAssertions> BeAssignableTo(
        Type expected, string? because = null, params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);

        if (Subject is null || !expected.IsAssignableFrom(Subject))
            AssertionFailure.Throw(Expression, $"to be assignable to {expected.Name}",
                Subject is null ? "found <null>" : $"{Subject.Name} is not", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the type derives from <typeparamref name="TBase"/>.</summary>
    /// <remarks>Strictly derives: a type is not derived from itself, which is how the callers read it.</remarks>
    public AndConstraint<TypeAssertions> BeDerivedFrom<TBase>(string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || Subject == typeof(TBase) || !typeof(TBase).IsAssignableFrom(Subject))
            AssertionFailure.Throw(Expression, $"to derive from {typeof(TBase).Name}",
                Subject is null ? "found <null>" : $"{Subject.Name} does not", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the type implements <typeparamref name="TInterface"/>.</summary>
    public AndConstraint<TypeAssertions> Implement<TInterface>(string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || !typeof(TInterface).IsAssignableFrom(Subject))
            AssertionFailure.Throw(Expression, $"to implement {typeof(TInterface).Name}",
                Subject is null ? "found <null>" : $"{Subject.Name} does not", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the type implements <typeparamref name="TInterface"/>.</summary>
    public AndConstraint<TypeAssertions> NotImplement<TInterface>(
        string? because = null, params object[] becauseArgs)
    {
        if (Subject is not null && typeof(TInterface).IsAssignableFrom(Subject))
            AssertionFailure.Throw(Expression, $"not to implement {typeof(TInterface).Name}",
                $"{Subject.Name} does", because, becauseArgs);

        return Ok();
    }
}
