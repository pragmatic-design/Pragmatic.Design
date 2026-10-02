using Pragmatic.Composition.Database;

namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Host-level: includes a module without associating it to a specific database.
///     Modules included with this overload don't generate DbContext registrations.
/// </summary>
/// <typeparam name="TModule">The module (boundary) type to include.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class IncludeAttribute<TModule> : Attribute
    where TModule : class
{
}

/// <summary>
///     Host-level: includes a module and wires it to a database.
///     The DbContext class name is auto-derived as <c>{BoundaryName}DbContext</c>.
/// </summary>
/// <typeparam name="TModule">The module (boundary) type to include.</typeparam>
/// <typeparam name="TDatabase">The database declaration (must derive from <see cref="PragmaticDatabase" />).</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class IncludeAttribute<TModule, TDatabase> : Attribute
    where TModule : class
    where TDatabase : PragmaticDatabase
{
}

/// <summary>
///     Host-level: includes a module, wires it to a database, and specifies the DbContext class name.
/// </summary>
/// <typeparam name="TModule">The module (boundary) type to include.</typeparam>
/// <typeparam name="TDatabase">The database declaration (must derive from <see cref="PragmaticDatabase" />).</typeparam>
/// <typeparam name="TDbContext">
///     The DbContext class to generate. Must be a class (not necessarily a DbContext at compile time —
///     the SG validates this). The generated class will be named after this type.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class IncludeAttribute<TModule, TDatabase, TDbContext> : Attribute
    where TModule : class
    where TDatabase : PragmaticDatabase
    where TDbContext : class
{
}
