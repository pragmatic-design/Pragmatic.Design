using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     A package says which of its operations need a boundary-keyed service, so the importer can supply
///     one.
/// </summary>
/// <remarks>
///     <para>
///         <c>DbContext</c> and <c>IUnitOfWork</c> are registered keyed by boundary. An assembly that
///         declares no <c>[Boundary]</c> — a package — has no key to give, so the invoker it generates
///         asks for them <b>unkeyed</b>, and its constructor is fixed in that compilation: an importer
///         cannot key it afterwards.
///     </para>
///     <para>
///         ⚠️ Six operations in <c>Pragmatic.Authorization.Management</c> are in exactly that state, and
///         nothing said so. The importer is where the boundary is known, so the package has to declare
///         the need and the importer answers it — the module declares, the composition composes.
///     </para>
/// </remarks>
public class PackageDeclaresItsKeyedNeedsTests : ActionsGeneratorTestBase
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Result;

        namespace TestPackage.Actions;
        """;

    /// <summary>
    ///     An action in a boundary-less assembly names the keyed service it cannot key itself.
    /// </summary>
    [Fact]
    public void AnActionWithNoBoundary_NamesTheKeyedServiceItNeeds()
    {
        var metadata = MetadataFor(Usings + """

            [DomainAction]
            public partial class CreateThing : DomainAction<Guid>
            {
                private DbContext _dbContext = null!;

                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.Empty));
            }
            """);

        metadata.Should().Contain("boundaryKeyedServices")
            .And.Contain("Microsoft.EntityFrameworkCore.DbContext");
    }

    /// <summary>
    ///     The control: an action that declares no boundary-keyed service says nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "the package declares its needs" is satisfied by a generator that writes the
    ///     property on every action, and the importer would then have to supply a boundary for packages
    ///     that never touch persistence — which is both packages this framework actually ships.
    /// </remarks>
    [Fact]
    public void AnActionWithoutOne_SaysNothing()
    {
        var metadata = MetadataFor(Usings + """

            public interface IThingService { }

            [DomainAction]
            public partial class CreateThing : DomainAction<Guid>
            {
                private IThingService _things = null!;

                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.Empty));
            }
            """);

        metadata.Should().NotContain("boundaryKeyedServices");
    }

    /// <summary>
    ///     The second control: an assembly that declares a boundary keys the field itself and says
    ///     nothing either.
    /// </summary>
    /// <remarks>
    ///     There is nothing for an importer to answer: the invoker's constructor already carries
    ///     <c>[FromKeyedServices]</c>. Announcing a need that is already met would make an importer
    ///     register a bridge that shadows the key it was given.
    /// </remarks>
    [Fact]
    public void AnActionInsideABoundary_SaysNothing()
    {
        var metadata = MetadataFor("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Result;

            namespace TestModule;

            [Boundary]
            public partial class TestBoundary { }

            [DomainAction]
            public partial class CreateThing : DomainAction<Guid>
            {
                private DbContext _dbContext = null!;

                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.Empty));
            }
            """);

        metadata.Should().NotContain("boundaryKeyedServices");
    }

    private static string MetadataFor(string source)
    {
        var result = RunGeneratorWithCompositionAndPersistence(source);

        var metadata = GetGeneratedSource(result, "_Metadata.Actions");
        metadata.Should().NotBeNull(
            "the assembly declares an action, so its metadata document is generated — without this "
            + "the assertions below would hold on a run that produced nothing");

        return metadata!;
    }
}
