using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Tests for <c>[ReadAccess&lt;TEntity&gt;]</c> generator output on a boundary.
///     The SG emits an assembly-level <c>[PragmaticModuleMetadata]</c> attribute carrying the
///     ReadAccess entity types, which the Persistence.EFCore generator later consumes to add
///     a cross-boundary <c>DbSet&lt;TEntity&gt;</c> (excluded from migrations).
///     This module-metadata emission only happens when Composition is referenced.
/// </summary>
public class ReadAccessGeneratorTests : ActionsGeneratorTestBase
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
    public void ReadAccess_OnBoundary_EmitsReadAccessTypesInModuleMetadata()
    {
        var source = CommonUsings + """

            namespace TestApp.Catalog
            {
                public class Property
                {
                    public Guid Id { get; set; }
                }
            }

            namespace TestApp.Booking
            {
                [Boundary]
                [ReadAccess<TestApp.Catalog.Property>]
                public partial class BookingBoundary;

                [DomainAction]
                public partial class CreateReservationAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGeneratorWithComposition(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var metadata = GetGeneratedSource(result, "ModuleMetadata");
        metadata.Should().NotBeNull();

        // Assembly-level module metadata carries the boundary and its ReadAccess types.
        metadata.Should().Contain("[assembly: PragmaticModuleMetadata(");
        metadata.Should().Contain("BoundaryType = typeof(global::TestApp.Booking.BookingBoundary)");
        metadata.Should().Contain("ReadAccessTypes = new[] {");
        metadata.Should().Contain("typeof(global::TestApp.Catalog.Property)");
    }

    [Fact]
    public void ReadAccess_MultipleAttributes_EmitsAllEntities()
    {
        var source = CommonUsings + """

            namespace TestApp.Catalog
            {
                public class Property { public Guid Id { get; set; } }
                public class Amenity { public Guid Id { get; set; } }
            }

            namespace TestApp.Booking
            {
                [Boundary]
                [ReadAccess<TestApp.Catalog.Property>]
                [ReadAccess<TestApp.Catalog.Amenity>]
                public partial class BookingBoundary;

                [DomainAction]
                public partial class CreateReservationAction : DomainAction<Guid>
                {
                    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
                }
            }
            """;

        var result = RunGeneratorWithComposition(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var metadata = GetGeneratedSource(result, "ModuleMetadata");
        metadata.Should().NotBeNull();

        metadata.Should().Contain("typeof(global::TestApp.Catalog.Property)");
        metadata.Should().Contain("typeof(global::TestApp.Catalog.Amenity)");
    }

    [Fact]
    public void NoReadAccess_ModuleMetadata_OmitsReadAccessTypes()
    {
        // Control: a boundary without [ReadAccess] still emits module metadata, but with no
        // ReadAccessTypes argument — confirming the array is driven by the attribute.
        var source = CommonUsings + """

            namespace TestApp.Plain;

            [Boundary]
            public partial class PlainBoundary;

            [DomainAction]
            public partial class DoWorkAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGeneratorWithComposition(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var metadata = GetGeneratedSource(result, "ModuleMetadata");
        metadata.Should().NotBeNull();

        metadata.Should().Contain("BoundaryType = typeof(global::TestApp.Plain.PlainBoundary)");
        metadata.Should().NotContain("ReadAccessTypes");
    }
}
