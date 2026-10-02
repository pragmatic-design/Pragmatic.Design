namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Imports an external package and says which boundary its operations belong to.
/// </summary>
/// <remarks>
///     <para>
///         Same as <see cref="UsePackageAttribute{TPackage}" /> in every other respect. The second type
///         argument answers one question the one-argument form cannot: a package declares no
///         <c>[Boundary]</c> of its own — that is what makes it a package — so an operation in it that
///         needs a <c>DbContext</c> or an <c>IUnitOfWork</c> has no key for services that are registered
///         <b>keyed by boundary</b>.
///     </para>
///     <para>
///         ⚠️ And it cannot get one later. The invoker is generated in the package's own compilation,
///         where no boundary exists, so its constructor asks for those services unkeyed and is fixed
///         before any importer sees it. The key therefore has to come from the composition, which is
///         exactly here: the importing module names a boundary it owns, and the generated registration
///         resolves the unkeyed request to that boundary's keyed instance.
///     </para>
///     <para>
///         A package that needs nothing keyed — which is both packages this framework ships — is
///         imported with <see cref="UsePackageAttribute{TPackage}" /> and needs no boundary. A package
///         whose metadata declares a boundary-keyed need and is imported without one is
///         <c>PRAG0449</c>: the application would start and fail at the first request that reaches the
///         operation.
///     </para>
/// </remarks>
/// <typeparam name="TPackage">The package definition type.</typeparam>
/// <typeparam name="TBoundary">
///     The boundary of the importing module whose <c>DbContext</c> and <c>IUnitOfWork</c> the package's
///     operations use. It must be a boundary this module declares — a package cannot be given the
///     boundary of a module that does not import it.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class UsePackageAttribute<TPackage, TBoundary> : Attribute
    where TPackage : class, IPackageDefinition
    where TBoundary : class
{
    /// <summary>
    ///     Overrides the default route prefix defined in <typeparamref name="TPackage" />.
    ///     When null, the package's <see cref="IPackageDefinition.RoutePrefix" /> is used.
    /// </summary>
    public string? RoutePrefix { get; set; }
}
