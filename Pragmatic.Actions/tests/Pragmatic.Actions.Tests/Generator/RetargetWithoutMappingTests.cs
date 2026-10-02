using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     PRAG0445 — <c>[MapProperty(Target = …)]</c> on a mutation in an assembly that does not reference
///     Pragmatic.Mapping.
/// </summary>
/// <remarks>
///     A mutation is a mapping under another classifier, so its write body is Mapping's wherever
///     Mapping is referenced — and only there is a declared target read. Without it the attribute
///     compiles, matches nothing, and the property is written nowhere: silence at the one place the
///     author was most explicit about intent. The attribute is declared in source here rather than
///     referenced, which is exactly the shape the diagnostic is about: the generator matches
///     <c>[MapProperty]</c> by metadata name, while the feature detector looks for
///     <c>MapFromAttribute&lt;T&gt;</c> to decide whether Mapping owns the body.
/// </remarks>
public class RetargetWithoutMappingTests
{
    private const string MapPropertyStub = """
        namespace Pragmatic.Mapping.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class MapPropertyAttribute : System.Attribute
            {
                public MapPropertyAttribute() { }
                public MapPropertyAttribute(string sourcePath) { }
                public string? Target { get; set; }
            }
        }
        """;

    /// <param name="attribute">What decorates the mutation's property.</param>
    private static string Source(string attribute) => $$"""
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        {{MapPropertyStub}}

        namespace TestApp;

        [Boundary]
        public partial class SalesBoundary;

        [Entity]
        public partial class Order : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Reference { get; private set; } = "";
            internal void SetReference(string value) => Reference = value;
        }

        [Mutation(Mode = MutationMode.Update)]
        public partial class UpdateOrderMutation : Mutation<Order>
        {
            public Guid Id { get; init; }

            {{attribute}}
            public string Code { get; init; } = "";
        }
        """;

    [Fact]
    public void ATargetNobodyReads_ReportsPrag0445()
    {
        var result = Run(Source("""[MapProperty(Target = "Reference")]"""));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0445").Should().BeTrue(
            "the declared target is read by Mapping, which this assembly does not reference, "
            + "so the property would be written nowhere");
    }

    /// <summary>
    ///     The control: the same property without the attribute. It then has no matching setter and is
    ///     PRAG0414's business, not this one — what must not happen is PRAG0445 firing for a property
    ///     that declared no target at all.
    /// </summary>
    [Fact]
    public void APropertyWithNoDeclaredTarget_IsNotReported()
    {
        var result = Run(Source(attribute: ""));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0445").Should().BeFalse();
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
            GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.SoftDeleteAttribute>(),
            GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
            GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
            GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
            GeneratorTestHelper.FromTypeAssembly(
                typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
            GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>());
}
