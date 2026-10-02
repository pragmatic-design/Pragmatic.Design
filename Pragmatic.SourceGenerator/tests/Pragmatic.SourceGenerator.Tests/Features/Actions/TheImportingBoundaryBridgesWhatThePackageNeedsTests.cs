using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     The boundary that imports a package bridges the services the package resolves unkeyed.
/// </summary>
/// <remarks>
///     <para>
///         A package declares no boundary — it does not know which application will import it — so
///         everything generated inside it asks for <c>DbContext</c> and <c>IUnitOfWork</c> plainly,
///         while a host registers them keyed by boundary. The constructor is fixed in the package's
///         own compilation and cannot be keyed afterwards, so the importing boundary registers the
///         plain names, pointing at its own.
///     </para>
///     <para>
///         ⚠️ Both, not only the one the package's <b>actions</b> happen to declare. A package that owns
///         entities has generated repositories that ask for the unit of work too; bridge only the
///         context and the container refuses to build: "Unable to resolve service for type
///         'IUnitOfWork'", naming a repository the author never wrote.
///     </para>
/// </remarks>
public class TheImportingBoundaryBridgesWhatThePackageNeedsTests
{
    private const string UnitOfWork = "global::Pragmatic.Persistence.Repository.IUnitOfWork";
    private const string DbContext = "global::Microsoft.EntityFrameworkCore.DbContext";

    /// <summary>A package asking for the context is bridged the unit of work as well.</summary>
    [Fact]
    public void APackageThatNeedsTheContext_IsAlsoBridgedTheUnitOfWork()
    {
        var source = Render(DbContext);

        source.Should().Contain($"services.AddScoped<{DbContext}>");
        source.Should().Contain($"services.AddScoped<{UnitOfWork}>",
            "what reads through the context writes through its unit of work");
        source.Should().Contain("typeof(global::App.Accounts.AccountsBoundary)");
    }

    /// <summary>And the other way round, for a package whose actions only save.</summary>
    [Fact]
    public void APackageThatNeedsTheUnitOfWork_IsAlsoBridgedTheContext()
    {
        var source = Render(UnitOfWork);

        source.Should().Contain($"services.AddScoped<{DbContext}>");
        source.Should().Contain($"services.AddScoped<{UnitOfWork}>");
    }

    /// <summary>
    ///     The control: a boundary that imports nothing bridges nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "the services are bridged" would be satisfied by registering an unkeyed
    ///     <c>DbContext</c> in every boundary of every application — which in a multi-boundary host is
    ///     one boundary answering for another's data.
    /// </remarks>
    [Fact]
    public void ABoundaryThatImportsNoPackage_BridgesNothing()
    {
        var source = Render(needed: null, packageKey: null);

        source.Should().NotContain($"services.AddScoped<{DbContext}>");
        source.Should().NotContain($"services.AddScoped<{UnitOfWork}>");
    }

    private static string Render(string? needed, string? packageKey = "global::App.Accounts.AccountsBoundary")
    {
        var registrations = needed is null
            ? ImmutableArray<PackageActionRegistration>.Empty
            : ImmutableArray.Create(new PackageActionRegistration
            {
                ActionType = "global::Pkg.Actions.DoSomething",
                InvokerType = "global::Pkg.Actions.DoSomething.Invoker",
                IsVoid = true,
                BoundaryKeyedServices = new EquatableArray<string>([needed]),
                SourceAssembly = "Pkg"
            });

        var boundary = new BoundaryModel
        {
            TypeName = "AccountsBoundary",
            FullTypeName = "global::App.Accounts.AccountsBoundary",
            Namespace = "App.Accounts",
            InterfaceName = "IAccountsActions",
            InternalInterfaceName = "IAccountsInternalActions",
            ImplementationName = "AccountsLocalActions",
            Accessibility = "public",
            PackageBoundaryKey = packageKey
        };

        return new BoundaryInterfaceTemplate(
            boundary,
            ImmutableArray<BoundaryMemberModel>.Empty,
            ImmutableArray<BoundaryMemberModel>.Empty,
            registrations,
            BoundaryOutputMode.Local).RenderOutput().Text;
    }
}
