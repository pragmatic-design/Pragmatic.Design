using System.Reflection;
using Pragmatic.Composition.Attributes;

namespace Pragmatic.Composition.Scanning;

/// <summary>
///     Configures assembly scanning for service registration.
///     Provides a fluent API similar to Scrutor.
/// </summary>
public interface IAssemblyScanner
{
    /// <summary>Adds the assembly that declares <typeparamref name="T" /> to the scan set.</summary>
    /// <typeparam name="T">A type whose declaring assembly should be scanned.</typeparam>
    /// <returns>The same scanner for fluent chaining.</returns>
    IAssemblyScanner FromAssemblyOf<T>();

    /// <summary>Adds the given assemblies to the scan set.</summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>The same scanner for fluent chaining.</returns>
    IAssemblyScanner FromAssemblies(params Assembly[] assemblies);

    /// <summary>
    ///     Loads and adds assemblies from the application base directory whose file name matches the
    ///     wildcard <paramref name="pattern" /> (e.g. <c>"MyApp.*"</c>). Reflection-based.
    /// </summary>
    /// <param name="pattern">A wildcard pattern (<c>*</c> = any sequence). Overly broad patterns are rejected.</param>
    /// <returns>The same scanner for fluent chaining.</returns>
    IAssemblyScanner FromAssembliesMatching(string pattern);

    /// <summary>Adds the calling assembly to the scan set.</summary>
    /// <returns>The same scanner for fluent chaining.</returns>
    IAssemblyScanner FromCallingAssembly();

    /// <summary>Adds the process entry assembly to the scan set, if one is available.</summary>
    /// <returns>The same scanner for fluent chaining.</returns>
    IAssemblyScanner FromEntryAssembly();

    /// <summary>
    ///     Adds assemblies discovered from the runtime dependency context. Project libraries are always
    ///     included; other libraries are included only when <paramref name="predicate" /> returns true for their name.
    /// </summary>
    /// <param name="predicate">Optional filter over library names; when null, only project libraries are included.</param>
    /// <returns>The same scanner for fluent chaining.</returns>
    IAssemblyScanner FromDependencyContext(Func<string, bool>? predicate = null);

    /// <summary>
    ///     Adds assemblies from the runtime dependency context whose library name starts with
    ///     <paramref name="prefix" /> (case-insensitive).
    /// </summary>
    /// <param name="prefix">The library name prefix to match.</param>
    /// <returns>The same scanner for fluent chaining.</returns>
    IAssemblyScanner FromDependencyContext(string prefix);

    /// <summary>
    ///     Selects non-abstract, non-generic classes from the accumulated assemblies, optionally narrowed
    ///     by <paramref name="filter" />.
    /// </summary>
    /// <param name="filter">Optional filter to narrow the matched classes.</param>
    /// <returns>A selector for choosing how the matched types are registered.</returns>
    ITypeSelector AddClasses(Action<ITypeFilter>? filter = null);

    /// <summary>Adds the single type <typeparamref name="T" /> to the registration set.</summary>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <returns>A selector for choosing how the type is registered.</returns>
    ITypeSelector AddType<T>() where T : class;

    /// <summary>Adds two explicit types to the registration set.</summary>
    /// <typeparam name="T1">The first type to register.</typeparam>
    /// <typeparam name="T2">The second type to register.</typeparam>
    /// <returns>A selector for choosing how the types are registered.</returns>
    ITypeSelector AddTypes<T1, T2>() where T1 : class where T2 : class;

    /// <summary>Adds three explicit types to the registration set.</summary>
    /// <typeparam name="T1">The first type to register.</typeparam>
    /// <typeparam name="T2">The second type to register.</typeparam>
    /// <typeparam name="T3">The third type to register.</typeparam>
    /// <returns>A selector for choosing how the types are registered.</returns>
    ITypeSelector AddTypes<T1, T2, T3>() where T1 : class where T2 : class where T3 : class;

    /// <summary>Adds the given types to the registration set, keeping only non-abstract classes.</summary>
    /// <param name="types">The candidate types to register.</param>
    /// <returns>A selector for choosing how the types are registered.</returns>
    ITypeSelector AddTypes(params Type[] types);
}

/// <summary>
///     Filters types during assembly scanning.
/// </summary>
public interface ITypeFilter
{
    /// <summary>Keeps only types assignable to <typeparamref name="T" /> (including open generic definitions).</summary>
    /// <typeparam name="T">The base type or interface to match against.</typeparam>
    /// <returns>The same filter for fluent chaining.</returns>
    ITypeFilter AssignableTo<T>();

    /// <summary>Keeps only types assignable to <paramref name="type" /> (including open generic definitions).</summary>
    /// <param name="type">The base type or interface to match against.</param>
    /// <returns>The same filter for fluent chaining.</returns>
    ITypeFilter AssignableTo(Type type);

    /// <summary>Keeps only types decorated with <typeparamref name="TAttribute" />.</summary>
    /// <typeparam name="TAttribute">The attribute that matched types must carry.</typeparam>
    /// <returns>The same filter for fluent chaining.</returns>
    ITypeFilter WithAttribute<TAttribute>() where TAttribute : Attribute;

    /// <summary>Keeps only types whose namespace exactly equals <paramref name="ns" />.</summary>
    /// <param name="ns">The exact namespace to match.</param>
    /// <returns>The same filter for fluent chaining.</returns>
    ITypeFilter InNamespace(string ns);

    /// <summary>Keeps only types in the namespace of <typeparamref name="T" /> or any nested namespace.</summary>
    /// <typeparam name="T">A type whose namespace (and sub-namespaces) defines the match.</typeparam>
    /// <returns>The same filter for fluent chaining.</returns>
    ITypeFilter InNamespaceOf<T>();

    /// <summary>Keeps only types for which <paramref name="predicate" /> returns true.</summary>
    /// <param name="predicate">The inclusion predicate.</param>
    /// <returns>The same filter for fluent chaining.</returns>
    ITypeFilter Where(Func<Type, bool> predicate);

    /// <summary>Excludes types for which <paramref name="predicate" /> returns true.</summary>
    /// <param name="predicate">The exclusion predicate.</param>
    /// <returns>The same filter for fluent chaining.</returns>
    ITypeFilter NotWhere(Func<Type, bool> predicate);
}

/// <summary>
///     Specifies how matched types should be registered.
/// </summary>
public interface ITypeSelector
{
    /// <summary>Registers each matched type against all interfaces it implements.</summary>
    /// <returns>A selector for choosing the service lifetime.</returns>
    ILifetimeSelector AsImplementedInterfaces();

    /// <summary>Registers each matched type as its own concrete type.</summary>
    /// <returns>A selector for choosing the service lifetime.</returns>
    ILifetimeSelector AsSelf();

    /// <summary>Registers each matched type both as itself and against all interfaces it implements.</summary>
    /// <returns>A selector for choosing the service lifetime.</returns>
    ILifetimeSelector AsSelfWithInterfaces();

    /// <summary>Registers each matched type against the service type <typeparamref name="T" />.</summary>
    /// <typeparam name="T">The service type to register against.</typeparam>
    /// <returns>A selector for choosing the service lifetime.</returns>
    ILifetimeSelector As<T>();

    /// <summary>Registers each matched type against the service <paramref name="type" />.</summary>
    /// <param name="type">The service type to register against.</param>
    /// <returns>A selector for choosing the service lifetime.</returns>
    ILifetimeSelector As(Type type);

    /// <summary>Registers each matched type against the service type produced by <paramref name="selector" />.</summary>
    /// <param name="selector">Maps an implementation type to the service type it should be registered as.</param>
    /// <returns>A selector for choosing the service lifetime.</returns>
    ILifetimeSelector As(Func<Type, Type> selector);

    /// <summary>
    ///     Registers each matched type against its convention-matching interface — the interface named
    ///     <c>I{TypeName}</c> (e.g. <c>Foo</c> registers against <c>IFoo</c>).
    /// </summary>
    /// <returns>A selector for choosing the service lifetime.</returns>
    ILifetimeSelector AsMatchingInterface();
}

/// <summary>
///     Specifies the lifetime for scanned services.
/// </summary>
public interface ILifetimeSelector
{
    /// <summary>Registers the selected services with singleton lifetime.</summary>
    void WithSingletonLifetime();

    /// <summary>Registers the selected services with scoped lifetime.</summary>
    void WithScopedLifetime();

    /// <summary>Registers the selected services with transient lifetime.</summary>
    void WithTransientLifetime();

    /// <summary>Registers the selected services with the given <paramref name="lifetime" />.</summary>
    /// <param name="lifetime">The service lifetime to use.</param>
    void WithLifetime(Lifetime lifetime);

    /// <summary>
    ///     Sets the strategy used when a service type is already registered (append, skip, replace, or throw).
    ///     Apply before the <c>With*Lifetime</c> call.
    /// </summary>
    /// <param name="strategy">The duplicate-registration strategy.</param>
    /// <returns>The same selector for fluent chaining.</returns>
    ILifetimeSelector UsingRegistrationStrategy(RegistrationStrategy strategy);
}
