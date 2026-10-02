using System.Linq.Expressions;
using Pragmatic.Specification.Specifications;

namespace Pragmatic.Specification;

/// <summary>
///     Base class for specifications with built-in composition support.
///     Provides And, Or, Not operations and operator overloads for fluent composition.
/// </summary>
/// <typeparam name="T">The entity type this specification applies to.</typeparam>
public abstract class Specification<T> : ISpecification<T>
{
    private readonly Lazy<Func<T, bool>> _compiled;

    /// <summary>
    ///     Initializes the lazy-compiled delegate for thread-safe first-use compilation.
    /// </summary>
    protected Specification()
    {
        _compiled = new Lazy<Func<T, bool>>(() => ToExpression().Compile());
    }

    /// <summary>
    ///     Returns the expression tree for this specification.
    ///     Override this in derived classes to provide the actual predicate.
    /// </summary>
    public abstract Expression<Func<T, bool>> ToExpression();

    /// <summary>
    ///     Evaluates whether the entity satisfies this specification.
    ///     The expression is compiled lazily on first use and cached (thread-safe).
    /// </summary>
    public bool IsSatisfiedBy(T entity)
    {
        return _compiled.Value(entity);
    }

    /// <summary>
    ///     Combines this specification with another using logical AND.
    ///     Accepts any ISpecification; non-Specification inputs are wrapped via their expression.
    /// </summary>
    /// <param name="other">The specification to combine with.</param>
    /// <returns>A new specification representing (this AND other).</returns>
    public Specification<T> And(ISpecification<T> other)
    {
        Ensure.Ensure.ThrowIfNull(other);
        return new AndSpecification<T>(this, AsSpecification(other));
    }

    /// <summary>
    ///     Combines this specification with another using logical OR.
    ///     Accepts any ISpecification; non-Specification inputs are wrapped via their expression.
    /// </summary>
    /// <param name="other">The specification to combine with.</param>
    /// <returns>A new specification representing (this OR other).</returns>
    public Specification<T> Or(ISpecification<T> other)
    {
        Ensure.Ensure.ThrowIfNull(other);
        return new OrSpecification<T>(this, AsSpecification(other));
    }

    /// <summary>
    ///     Negates this specification.
    /// </summary>
    /// <returns>A new specification representing NOT(this).</returns>
    public Specification<T> Not()
    {
        return new NotSpecification<T>(this);
    }

    /// <summary>
    ///     Conditionally combines this specification with another using AND.
    ///     If the condition is false, returns this specification unchanged.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="other">The specification to combine with if condition is true.</param>
    /// <returns>A new specification if condition is true; otherwise, this specification.</returns>
    public Specification<T> AndIf(bool condition, ISpecification<T> other)
    {
        return condition ? And(other) : this;
    }

    /// <summary>
    ///     Conditionally combines this specification with another using AND.
    ///     The factory is only invoked when the condition is true, avoiding unnecessary allocation.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="specFactory">Factory that creates the specification to combine with.</param>
    /// <returns>A new specification if condition is true; otherwise, this specification.</returns>
    public Specification<T> AndIf(bool condition, Func<Specification<T>> specFactory)
    {
        return condition ? And(specFactory()) : this;
    }

    /// <summary>
    ///     Conditionally combines this specification with another using OR.
    ///     If the condition is false, returns this specification unchanged.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="other">The specification to combine with if condition is true.</param>
    /// <returns>A new specification if condition is true; otherwise, this specification.</returns>
    public Specification<T> OrIf(bool condition, ISpecification<T> other)
    {
        return condition ? Or(other) : this;
    }

    /// <summary>
    ///     Conditionally combines this specification with another using OR.
    ///     The factory is only invoked when the condition is true, avoiding unnecessary allocation.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="specFactory">Factory that creates the specification to combine with.</param>
    /// <returns>A new specification if condition is true; otherwise, this specification.</returns>
    public Specification<T> OrIf(bool condition, Func<Specification<T>> specFactory)
    {
        return condition ? Or(specFactory()) : this;
    }

    /// <summary>
    ///     Logical AND operator for specification composition.
    /// </summary>
    public static Specification<T> operator &(Specification<T> left, Specification<T> right)
    {
        return left.And(right);
    }

    /// <summary>
    ///     Logical OR operator for specification composition.
    /// </summary>
    public static Specification<T> operator |(Specification<T> left, Specification<T> right)
    {
        return left.Or(right);
    }

    /// <summary>
    ///     Logical NOT operator for specification negation.
    /// </summary>
    public static Specification<T> operator !(Specification<T> specification)
    {
        return specification.Not();
    }

    /// <summary>
    ///     Implicit conversion from expression to specification.
    /// </summary>
    public static implicit operator Specification<T>(Expression<Func<T, bool>> expression)
    {
        return new ExpressionSpecification<T>(expression);
    }

    /// <summary>
    ///     Returns a debug name for this specification.
    ///     Override <see cref="SpecificationName"/> in derived classes to customise.
    /// </summary>
    public override string ToString() => SpecificationName;

    /// <summary>
    ///     The name shown in <see cref="ToString"/>. Defaults to the concrete class's short name;
    ///     override in subclasses to provide a more descriptive label.
    /// </summary>
    protected virtual string SpecificationName => GetType().Name;

    /// <summary>
    ///     Converts an ISpecification to a Specification, wrapping if necessary.
    /// </summary>
    internal static Specification<T> AsSpecification(ISpecification<T> spec)
    {
        return spec as Specification<T> ?? new ExpressionSpecification<T>(spec.ToExpression());
    }
}