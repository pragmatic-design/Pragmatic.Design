using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Mutation;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     <c>PrepareMutationAsync</c> — where the generated invoker loads what <c>[LoadEntity]</c> declares —
///     runs after authorization and before the mutation's own row is loaded, and its error is the answer.
/// </summary>
/// <remarks>
///     The same place as <c>PrepareActionAsync</c> on an action: after the filters, so a caller who may not
///     run the mutation cannot tell an existing row from a missing one by 404-vs-403.
/// </remarks>
public class AMutationPreparesWhatItDeclaresTests
{
    private sealed class Team
    {
        public string Name { get; private set; } = "";

        internal void Rename(string value) => Name = value;
    }

    private sealed class RenameTeam : Mutation<Team>
    {
        public required string Name { get; init; }

        public List<string>? Steps { get; set; }

        public override Task<Result<Team, IError>> ApplyAsync(Team entity, CancellationToken ct = default)
        {
            Steps?.Add("apply");
            entity.Rename(Name);
            return Task.FromResult<Result<Team, IError>>(entity);
        }
    }

    private sealed class Invoker(IServiceProvider sp, IError? prepareError) : MutationInvoker<RenameTeam, Team>(sp)
    {
        protected override void InjectDependencies(RenameTeam mutation) { }

        protected override Task<IError?> PrepareMutationAsync(RenameTeam mutation, CancellationToken ct)
        {
            mutation.Steps?.Add("prepare");
            return Task.FromResult(prepareError);
        }

        protected override Task<Team?> LoadEntityAsync(RenameTeam mutation, CancellationToken ct)
        {
            mutation.Steps?.Add("load");
            return Task.FromResult<Team?>(new Team());
        }

        protected override Team CreateEntity() => new();
        protected override MutationMode GetMode() => MutationMode.Update;
        protected override string? GetEntityIdString(RenameTeam mutation) => "team-1";
        protected override void PersistNew(Team entity) { }
        protected override void DeleteEntity(Team entity) { }
        protected override Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class DenyingFilter : IActionFilter<RenameTeam>
    {
        public Task<VoidResult<IError>> BeforeExecuteAsync(RenameTeam action, CancellationToken ct)
            => Task.FromResult(VoidResult<IError>.Failure(ForbiddenError.ActionDenied("RenameTeam")));
    }

    [Fact]
    public async Task ThePreparation_RunsBeforeTheRowIsLoadedAndTheMutationApplied()
    {
        using var provider = Provider();
        var mutation = new RenameTeam { Name = "Platform", Steps = [] };

        var result = await new Invoker(provider, prepareError: null).InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        mutation.Steps.Should().Equal("prepare", "load", "apply");
    }

    /// <summary>A declared entity that is not there is the answer: nothing is loaded or written after it.</summary>
    [Fact]
    public async Task APreparationError_IsTheAnswer()
    {
        using var provider = Provider();
        var mutation = new RenameTeam { Name = "Platform", Steps = [] };

        var result = await new Invoker(provider, NotFoundError.For("Employee", "e-1")).InvokeAsync(mutation);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
        mutation.Steps.Should().Equal("prepare");
    }

    /// <summary>The control for the order: a caller refused by a filter never reaches the load.</summary>
    [Fact]
    public async Task ARefusedCaller_NeverReachesThePreparation()
    {
        using var provider = Provider(services => services.AddSingleton<IActionFilter<RenameTeam>, DenyingFilter>());
        var mutation = new RenameTeam { Name = "Platform", Steps = [] };

        var result = await new Invoker(provider, NotFoundError.For("Employee", "e-1")).InvokeAsync(mutation);

        result.Error.Should().BeOfType<ForbiddenError>("the refusal comes first, whatever the declared entity would say");
        mutation.Steps.Should().BeEmpty();
    }

    private static ServiceProvider Provider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new ValidationOptions()));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
