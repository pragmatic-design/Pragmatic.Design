using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Mapping;

/// <summary>
///     A projection computes a <c>[Projectable]</c> member of an entity compiled into
///     another assembly.
/// </summary>
/// <remarks>
///     <para>
///         The projection writes the member's body so the database computes it; EF Core cannot see into
///         a getter and would run it in memory, over navigations nobody loaded. The body was
///         read from the member's syntax — and an entity compiled into a referenced assembly has none
///         in the compilation that maps it, so the projection fell back to the getter, in silence.
///     </para>
///     <para>
///         Two compilations, the way an application is split: the module that declares the entity,
///         generated and referenced, and the one that maps it. The module publishes the body on the
///         member's <c>Expr</c> property; the mapping reads it from there.
///     </para>
/// </remarks>
public class AProjectableMemberFromAReferencedAssemblyTests
{
    private const string Usings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Logging;

        """;

    private const string Module = Usings + """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Attributes;

        namespace Billing;

        [Boundary]
        public partial class BillingBoundary;

        public enum InvoiceKind { Standard, Credit }

        [Entity]
        [BelongsTo<BillingBoundary>]
        public partial class Invoice : IEntity
        {
            public decimal Total { get; private set; }
            public decimal Discount { get; private set; }
            public InvoiceKind Kind { get; private set; }

            [Projectable]
            public decimal Due => Kind == InvoiceKind.Credit ? 0m : Total - Discount;
        }
        """;

    private const string Mapper = Usings + """
        using Pragmatic.Mapping.Attributes;

        namespace Reports;

        [MapFrom<global::Billing.Invoice>]
        [GenerateProjection]
        public partial class InvoiceLineDto
        {
            public decimal Total { get; init; }
            public decimal Due { get; init; }
        }
        """;

    /// <summary>The body of Invoice.Due, over the projection's source.</summary>
    private const string DueBody =
        "entity.Kind == global::Billing.InvoiceKind.Credit ? 0m : entity.Total - entity.Discount";

    [Fact]
    public void TheProjection_ComputesTheMember_FromTheBodyItsModulePublished()
    {
        var module = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Billing.Module", Module, References);
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Mapper, [.. References, module]);

        GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString()).Should().BeEmpty();
        var projection = ProjectionOf(GeneratorTestHelper.GetGeneratedSource(result, "InvoiceLineDto.Mapping"));
        projection.Should().Contain(DueBody);
        projection.Should().NotContain("Due = entity.Due",
            "the getter is no column: EF Core would compute it in memory");
    }

    /// <summary>The control: a plain column of the same entity is read as it is.</summary>
    [Fact]
    public void APlainColumnOfTheSameEntity_IsReadAsItIs()
    {
        var module = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Billing.Module", Module, References);
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Mapper, [.. References, module]);

        ProjectionOf(GeneratorTestHelper.GetGeneratedSource(result, "InvoiceLineDto.Mapping"))
            .Should().Contain("Total = entity.Total");
    }

    private static string ProjectionOf(string? mapping)
    {
        mapping.Should().NotBeNull("the DTO is mapped");
        var start = mapping!.IndexOf("Projection { get; } =", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the DTO asks for a projection");
        return mapping[start..];
    }

    // The inventory corpus's set (TheRegistrationAppliesUseDatabaseTests), which is what a module with
    // entities compiles against: FromType adds one assembly and not its dependencies.
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        GeneratorTestHelper.FromType<global::System.ComponentModel.TypeConverterAttribute>(),
        .. ByName(
            "System.Text.Json",
            "System.Linq.Queryable",
            "System.Linq.Expressions",
            "System.ComponentModel.Annotations",
            "System.Diagnostics.DiagnosticSource",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Hosting.Abstractions",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Extensions.Configuration.Abstractions",
            "System.Private.Uri",
            "System.Security.Claims",
            "System.ComponentModel.TypeConverter",
            "Pragmatic.Result",
            "Pragmatic.Ensure",
            "Pragmatic.Specification",
            "Pragmatic.Mapping",
            "Pragmatic.Mapping.EFCore",
            "Pragmatic.Validation",
            "Pragmatic.Caching",
            "Pragmatic.Events",
            "Pragmatic.Authorization",
            "Pragmatic.Actions"),
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException(
                $"'{n}' does not resolve in the test process. Add the project reference, or the "
                + "compilation silently loses whatever needs it."));
}
