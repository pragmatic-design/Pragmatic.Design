using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     <c>[LoadCurrentUser]</c> on an action or a mutation: the invoker loads the signed-in user's entity
///     through the generated resolver, answers 401 / 404 itself, and hands it to a generated field.
/// </summary>
/// <remarks>
///     Time off wrote the same three lines in six operations — resolve the employee, 404 when
///     there is none — because <c>[LoadEntity]</c> takes a key the operation carries, and the signed-in
///     user's entity is named by nobody's key.
/// </remarks>
public class AnOperationLoadsTheSignedInUserTests
{
    /// <summary>What Identity.Persistence would bring: the marker that turns the user resolver on.</summary>
    private const string Model = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Identity;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Result;

        namespace Pragmatic.Identity.Persistence.Entities
        {
            public class RolePermission { }
        }

        namespace TestApp
        {
            [Boundary]
            public partial class SalesBoundary { }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string? Note { get; set; }
            }

            [PragmaticDbContext("Sales")]
            public partial class SalesDbContext { }

            [DomainAction]
            [LoadCurrentUser]
            public partial class GreetAction : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<string, IError>>(_currentCustomer.Email);
            }

            [Mutation(Mode = MutationMode.Update)]
            [LoadCurrentUser(FieldName = "_signer")]
            public partial class SignOrderMutation : Mutation<Order>
            {
                public required Guid Id { get; init; }

                public override Task<Result<Order, IError>> ApplyAsync(Order entity, CancellationToken ct = default)
                {
                    entity.Note = _signer.Email;
                    return Task.FromResult<Result<Order, IError>>(entity);
                }
            }

            [DomainAction]
            public partial class PlainAction : DomainAction<bool>
            {
                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
        }
        """;

    private const string UserEntity = """

        namespace TestApp
        {
            [Entity]
            [BelongsTo<SalesBoundary>]
            [PragmaticUser]
            public partial class Customer : IEntity
            {
                public string ExternalIdentityKey { get; set; } = "";
                public string Email { get; set; } = "";
            }
        }
        """;

    [Fact]
    public void AnAction_IsGivenTheSignedInUsersEntity()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + UserEntity);

        Source(sources, "GreetAction.LoadCurrentUser").Should().Contain("global::TestApp.Customer _currentCustomer");

        var invoker = Source(sources, "GreetAction.Invoker");
        invoker.Should().Contain("new global::TestApp.CustomerResolver(");
        invoker.Should().Contain("UnauthorizedError.Create()");
        invoker.Should().Contain("NotFoundError.For(\"Customer\", __signedIn.Id)");
        invoker.Should().Contain("action.SetCurrentUser(__signedInUser);");
    }

    [Fact]
    public void AMutation_IsGivenItToo_UnderTheFieldNameItChose()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + UserEntity);

        Source(sources, "SignOrderMutation.LoadCurrentUser").Should().Contain("global::TestApp.Customer _signer");
        Source(sources, "SignOrderMutation.MutationInvoker").Should().Contain("mutation.SetCurrentUser(__signedInUser);");
    }

    /// <summary>The control: an operation that does not declare it loads nobody.</summary>
    [Fact]
    public void AnOperationWithoutIt_LoadsNobody()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + UserEntity);

        sources.Keys.Should().NotContain(k => k.Contains("PlainAction.LoadCurrentUser"));
        Source(sources, "PlainAction.Invoker").Should().NotContain("SetCurrentUser");
    }

    [Fact]
    public void TheOperations_Compile()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model + UserEntity,
            static path => path.Contains("GreetAction") || path.Contains("SignOrderMutation")
                           || path.Contains("PlainAction") || path.EndsWith("TestSource.cs"));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    [Fact]
    public void AModuleWithNoUserEntity_IsReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model);

        diagnostics.Where(d => d.Id == "PRAG0451").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("GreetAction") && m.Contains("no [PragmaticUser] entity"));
    }

    private static string Source(System.Collections.Generic.Dictionary<string, string> sources, string hint)
    {
        var match = sources.FirstOrDefault(s => s.Key.Contains(hint));
        match.Value.Should().NotBeNull($"{hint} is generated; generated: {string.Join(", ", sources.Keys)}");
        return match.Value;
    }
}
