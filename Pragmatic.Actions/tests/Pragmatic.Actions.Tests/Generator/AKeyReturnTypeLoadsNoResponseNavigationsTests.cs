using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     The invoker loads what the response DTO reads only when the DTO is the response.
/// </summary>
/// <remarks>
///     With <c>ReturnType = Id</c> or <c>LogicalKey</c> the mutation answers the key, and a
///     <c>[ReturnsDto&lt;T&gt;]</c> beside it is read by nothing (PRAG0535 says so on the endpoint).
///     Including <c>{Dto}.RequiredNavigations</c> on the load would be a join for a response nobody
///     builds.
/// </remarks>
public class AKeyReturnTypeLoadsNoResponseNavigationsTests
{
    private const string EfCorePresence = """
        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }
        """;

    /// <param name="mutationAttribute">The mutation's own declaration.</param>
    /// <param name="logicKey">What the entity's key property declares.</param>
    private static string Source(string mutationAttribute, string logicKey = "[LogicKey]") => $$"""
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        {{EfCorePresence}}

        namespace TestApp
        {
            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order : IEntity
            {
                {{logicKey}}
                public string Reference { get; private set; } = "";

                public string Label { get; set; } = "";
            }

            [MapFrom<Order>]
            public partial class OrderDto
            {
                public Guid Id { get; init; }
                public string Label { get; init; } = "";
            }

            {{mutationAttribute}}
            [ReturnsDto<OrderDto>]
            public partial class RenameOrderMutation : Mutation<Order>
            {
                public Guid Id { get; init; }
                public string Label { get; init; } = "";
            }
        }
        """;

    [Theory]
    [InlineData("Id")]
    [InlineData("LogicalKey")]
    public void AKeyReturnType_DoesNotLoadTheDtosNavigations(string returnType)
    {
        var invoker = Invoker(Source($"[Mutation(Mode = MutationMode.Update, ReturnType = MutationReturnType.{returnType})]"));

        invoker.Should().NotContain("OrderDto.RequiredNavigations",
            "the mutation answers its key, so nothing reads what the DTO would have needed");
    }

    /// <summary>The control: the DTO is the response, so the load brings what it reads.</summary>
    [Fact]
    public void TheEntityReturnType_LoadsTheDtosNavigations()
    {
        var invoker = Invoker(Source("[Mutation(Mode = MutationMode.Update)]"));

        invoker.Should().Contain("OrderDto.RequiredNavigations");
    }

    /// <summary>
    ///     A <c>LogicalKey</c> with no key to answer falls back to the entity (PRAG0403 is reported on
    ///     the mutation), and the endpoint then answers the DTO — so the load still brings what it reads.
    /// </summary>
    [Fact]
    public void ALogicalKeyWithNoKey_FallsBackToTheEntity_AndLoadsTheDtosNavigations()
    {
        var invoker = Invoker(Source(
            "[Mutation(Mode = MutationMode.Update, ReturnType = MutationReturnType.LogicalKey)]", logicKey: ""));

        invoker.Should().Contain("OrderDto.RequiredNavigations");
    }

    private static string Invoker(string source)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            [
                GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.EntityAttribute>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Authorization.Policy.ResourcePolicy)),
                GeneratorTestHelper.FromType<Pragmatic.Mapping.Attributes.MapFromAttribute<object>>(),
                GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
                GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
                GeneratorTestHelper.FromTypeAssembly(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
                GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
            ]);

        // The source has to be the one the test describes: an unresolved [ReturnsDto] reads as "no DTO",
        // and every assertion below would then hold for that reason instead.
        var errors = GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("TestSource") == true)
            .Select(d => d.ToString())
            .ToList();
        errors.Should().BeEmpty(string.Join(" | ", errors));

        var invoker = GeneratorTestHelper.GetGeneratedSource(result, "RenameOrderMutation.MutationInvoker");
        invoker.Should().NotBeNull();
        return invoker!;
    }
}
