using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     A second input shape for the same operation.
/// </summary>
/// <remarks>
///     <para>
///         The same operation can be reachable from two different shapes — a public one and an
///         internal one, or a new one next to the one old clients still send. The endpoint generates
///         the first DTO; the second one needs a way in.
///     </para>
///     <para>
///         ⚠️ The natural shape already exists: <c>[MapTo&lt;TMutation&gt;]</c> — a mutation is a set of
///         writable properties like any other target. No dedicated mechanism is needed.
///     </para>
/// </remarks>
public class ASecondRequestShapeTests
{
    [Fact]
    public void ADtoThatMapsOntoAMutation_Compiles()
    {
        var result = RunGenerator("""
            using System;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Mapping.Attributes;
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
            public partial class Order : IEntity
            {
                public string Reference { get; set; } = "";
            }

            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateOrderMutation : Mutation<Order>
            {
                public Guid Id { get; init; }
                public string Reference { get; init; } = "";
            }

            // The second shape: a different name on the wire for the same operation.
            [MapTo<UpdateOrderMutation>]
            public partial class RenameOrderRequest
            {
                public Guid Id { get; init; }
                public string Reference { get; init; } = "";
            }
            """);

        // Only the file this case is about: the fixture is hand-built and does not carry the reference
        // closure of a real module, so the other errors are bench noise.
        var errors = GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("RenameOrderRequest") == true)
            .Select(d => d.ToString())
            .ToList();

        errors.Should().BeEmpty(string.Join("; ", errors));

        // ⚠️ And something must still be there: «it compiles» is true of an empty file too. The second
        // shape exists to BUILD the operation, so ToEntity is what must exist — and ApplyTo must not,
        // because a mutation has `init` properties and «change what is already there» means nothing on
        // an object that cannot change.
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "RenameOrderRequest.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().Contain("ToEntity()",
            "the second shape builds the operation");
        generated.Should().NotContain("public void ApplyToLoaded",
            "and does not offer a method that could not compile on an immutable target");
    }

    private static SourceGenRunResult RunGenerator(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
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
            ]);
}
