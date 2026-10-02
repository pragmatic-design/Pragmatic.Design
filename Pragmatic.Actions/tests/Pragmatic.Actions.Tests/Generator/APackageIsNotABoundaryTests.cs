using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[BelongsTo&lt;TPackage&gt;]</c> is not <c>[BelongsTo&lt;TBoundary&gt;]</c>: a
///     package has no unit of work, and its actions' invokers must not ask for one.
/// </summary>
/// <remarks>
///     <para>
///         A package's actions declare their package this way. Read as a boundary, the invoker would
///         take an <c>IUnitOfWork</c> keyed by the <b>package</b> type — which nothing registers — so
///         importing the package would stop the host at container validation, with the error naming a
///         generated invoker and neither the attribute nor the remedy: every test that boots the host
///         fails at once.
///     </para>
///     <para>
///         ⚠️ The question is asked of <c>IPackageDefinition</c> and not of <c>IBoundary</c>, because
///         the boundary interface is added by this generator: a boundary declared in the compilation
///         being analysed does not implement it yet, and the answer would be wrong for every module's
///         own boundary. The control below is what pins that the ordinary case still gets its unit of
///         work.
///     </para>
///     <para>
///         ⚠️ <b>The runner is the one with Persistence as well as Composition</b>, and that is not a
///         detail: without Persistence the source's <c>using Pragmatic.Persistence.Entity;</c> does
///         not resolve, <c>[BelongsTo&lt;T&gt;]</c> is an error symbol nobody reads, and the action
///         falls back to the single boundary of the assembly — so the test would pass its control and
///         fail its subject for a reason that has nothing to do with either. The base's own remark on
///         that runner says why: what a package's metadata says about a boundary-keyed dependency
///         needs both reference sets at once.
///     </para>
/// </remarks>
public class APackageIsNotABoundaryTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.Admin;

        public sealed class AdminPackage : IPackageDefinition
        {
            public static string PackageName => "TestApp.Admin";
            public static string? RoutePrefix => "admin";
            public static string? Description => null;
        }

        [Boundary]
        public partial class AdminBoundary;

        [DomainAction]
        [BelongsTo<AdminPackage>]
        public partial class ReadSetting : DomainAction<string>
        {
            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success("value"));
        }

        [DomainAction]
        [BelongsTo<AdminBoundary>]
        public partial class WriteSetting : DomainAction<string>
        {
            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success("value"));
        }
        """;

    [Fact]
    public void AnActionThatBelongsToAPackage_AsksForNoUnitOfWork()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithCompositionAndPersistence(Source), "ReadSetting.Invoker");

        invoker.Should().NotBeNull("the action still gets an invoker");
        invoker!.Should().NotContain("IUnitOfWork",
            "a package has no unit of work, and asking for one keyed by the package type is a service "
            + "nobody registers — the host does not start");
    }

    /// <summary>
    ///     The control: an action that belongs to a real boundary still takes the boundary's unit of
    ///     work, keyed by it.
    /// </summary>
    /// <remarks>
    ///     Without this, "no IUnitOfWork" would be satisfied by an invoker that lost its commit
    ///     entirely — which would break every writing action in every module instead of fixing one
    ///     package.
    /// </remarks>
    [Fact]
    public void AnActionThatBelongsToABoundary_StillTakesItsUnitOfWork()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithCompositionAndPersistence(Source), "WriteSetting.Invoker");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("IUnitOfWork", "the boundary is what has one");
        invoker.Should().Contain("FromKeyedServices(typeof(global::TestApp.Admin.AdminBoundary))",
            "and it is keyed by the boundary, which is how the host registers it");
    }
}
