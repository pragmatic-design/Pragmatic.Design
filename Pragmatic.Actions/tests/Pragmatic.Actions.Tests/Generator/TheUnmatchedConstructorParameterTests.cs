using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     A constructor parameter the mutation does not carry.
/// </summary>
/// <remarks>
///     <para>
///         There are two cases, and neither is <c>default</c>. A parameter with a default value of its
///         own is <b>omitted</b>, so that value applies; a required one that no property matches has no
///         way out, and the build stops with <c>PRAG0446</c>.
///     </para>
///     <para>
///         ⚠️ With positional arguments, what is missing would become <c>default</c>: a
///         <c>string currency = "EUR"</c> would reach the entity as <c>null</c>, and a required
///         parameter would build a broken entity with nothing saying so. Mapping's <c>PRAG0316</c>
///         cannot fire on this path, which is why the invoker has a diagnostic of its own.
///     </para>
/// </remarks>
public class TheUnmatchedConstructorParameterTests
{
    private const string Source = """
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;

        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Invoice : IEntity
        {
            // The constructor requires two values; the mutation carries only one.
            public Invoice(string number, string currency)
            {
                Number = number;
                Currency = currency;
            }

            public string Number { get; private set; }
            public string Currency { get; private set; }
        }

        [Mutation(Mode = MutationMode.Create)]
        public partial class CreateInvoiceMutation : Mutation<Invoice>
        {
            public Guid Id { get; init; }
            public string Number { get; init; } = "";
        }
        """;

    [Fact]
    public void AMandatoryParameterWithNoProperty_StopsTheBuild()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0446").Should().BeTrue(
            "building with default a value the constructor requires produces a broken entity");
    }

    /// <summary>A parameter with a default value of its own is omitted, not zeroed.</summary>
    /// <remarks>
    ///     It is also the control case for the first: if PRAG0446 fired for any unmatched parameter —
    ///     the opposite error — this would turn red at once, instead of finding out later that a
    ///     legitimate create no longer compiles.
    /// </remarks>
    [Fact]
    public void AnOptionalParameter_IsOmittedSoItsOwnDefaultApplies()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source.Replace("string currency)", """string currency = "EUR")"""), References);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0446").Should().BeFalse(
            "the parameter knows what to do when nobody tells it anything");

        var invoker = GeneratorTestHelper.GetGeneratedSource(result, "CreateInvoiceMutation.MutationInvoker");
        invoker.Should().NotBeNull();
        invoker!.Should().Contain("new global::TestApp.Invoice(number: this.Number)",
            "named and without currency: omitting it is what makes \"EUR\" apply");
        invoker.Should().NotContain("default)",
            "default is not the parameter's own default, and that is the difference this case measures");
    }

    private static readonly Microsoft.CodeAnalysis.MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
        GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.SoftDeleteAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Mapping.Attributes.MapToAttribute<object>>(),
        GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
        GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
        GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
        GeneratorTestHelper.FromTypeAssembly(
            typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
        GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
    ];
}
