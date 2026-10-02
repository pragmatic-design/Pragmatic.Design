namespace Pragmatic.Actions.Mutation;

/// <summary>
///     Specifies navigation properties to eagerly load when loading the entity for a mutation.
///     Applied to mutation classes to declare <c>.Include()</c> paths.
/// </summary>
/// <remarks>
///     <para>
///         Named EagerLoad, not Include: <c>[Include&lt;TModule, TDatabase&gt;]</c> in
///         <c>Pragmatic.Composition.Attributes</c> is the host topology attribute, and the two share
///         nothing but a word borrowed from EF. With both usings in scope the compiler reported
///         CS0104 and stopped, which cost a consumer a build cycle to diagnose.
///     </para>
///     <para>
///         Multiple attributes can be applied for multiple include paths.
///         The MutationInvoker uses these to build the query when loading the entity.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Mutation(Mode = MutationMode.Update)]
/// [EagerLoad("Lines")]
/// [EagerLoad("Lines.Product")]
/// public partial class UpdateOrder : Mutation&lt;Order&gt;
/// {
///     public required Guid Id { get; init; }
///     // ...
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class EagerLoadAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance with the navigation path to include.
    /// </summary>
    /// <param name="navigationPath">
    ///     The dot-separated navigation path to eagerly load (e.g., "Lines" or "Lines.Product").
    /// </param>
    public EagerLoadAttribute(string navigationPath)
    {
        NavigationPath = navigationPath;
    }

    /// <summary>
    ///     Gets the navigation path to eagerly load.
    /// </summary>
    public string NavigationPath { get; }
}
