namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Declares that <typeparamref name="T"/> should get a generated, member-by-member
///     <c>BeEquivalentTo</c>.
/// </summary>
/// <remarks>
///     <para>
///         <c>Equals</c> answers whether two values differ. This answers <b>where</b>:
///         <c>Expected order.Total to be 42, but found 43</c> instead of two objects printed side by
///         side for the reader to diff by eye. On a type without value equality it is also the only
///         correct answer — plain <c>Equals</c> would compare references and quietly fail.
///     </para>
///     <para>
///         Declared per test assembly, as the mocks are:
///         <c>[assembly: GenerateComparer&lt;OrderDto&gt;]</c>. The declarations double as the
///         inventory of what a suite compares structurally.
///     </para>
/// </remarks>
/// <typeparam name="T">The type to compare member by member.</typeparam>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GenerateComparerAttribute<T> : Attribute;
