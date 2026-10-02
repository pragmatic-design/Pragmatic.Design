using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Tests for <c>[Boundary(Visibility = BoundaryVisibility.Internal)]</c> generator output.
///     An internal boundary generates only a standalone internal interface (no public interface,
///     and the internal interface does NOT extend a public base). The local implementation still
///     implements the internal interface.
/// </summary>
public class BoundaryVisibilityGeneratorTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Result;
        """;

    [Fact]
    public void InternalBoundary_GeneratesStandaloneInternalInterface_NoPublicInterface()
    {
        var source = CommonUsings + """

            namespace TestApp.Internals;

            [Boundary(Visibility = BoundaryVisibility.Internal)]
            public partial class AdminBoundary;

            [DomainAction]
            public partial class DoAdminWorkAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Standalone internal interface — declared but NOT extending the public base.
        generated.Should().Contain("internal interface IAdminInternalActions");
        generated.Should().NotContain("internal interface IAdminInternalActions : IAdminActions");

        // No public interface declaration is emitted for an internal boundary.
        generated.Should().NotContain("public interface IAdminActions");

        // The action is exposed on the internal interface.
        generated.Should().Contain("DoAdminWork(");
    }

    [Fact]
    public void InternalBoundary_LocalImplementation_ImplementsInternalInterface()
    {
        var source = CommonUsings + """

            namespace TestApp.Internals;

            [Boundary(Visibility = BoundaryVisibility.Internal)]
            public partial class AdminBoundary;

            [DomainAction]
            public partial class DoAdminWorkAction : VoidDomainAction
            {
                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        // Local impl still implements the internal interface.
        generated.Should().Contain("AdminLocalActions : IAdminInternalActions");
    }

    [Fact]
    public void PublicBoundary_GeneratesBothInterfaces()
    {
        // Control: default visibility (Public) still emits a public interface, so the
        // Internal-only behaviour above is genuinely attributable to the visibility flag.
        var source = CommonUsings + """

            namespace TestApp.Publics;

            [Boundary(Visibility = BoundaryVisibility.Public)]
            public partial class AdminBoundary;

            [DomainAction]
            public partial class DoAdminWorkAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetBoundarySource(result);
        generated.Should().NotBeNull();

        generated.Should().Contain("public interface IAdminActions");
        generated.Should().Contain("internal interface IAdminInternalActions : IAdminActions");
    }
}
