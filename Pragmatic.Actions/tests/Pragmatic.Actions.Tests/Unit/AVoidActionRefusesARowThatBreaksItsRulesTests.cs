using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Mutation;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     A void action checks the <c>[Invariant]</c> rules of the entity it loaded after its body and before
///     the save, as a typed action does.
/// </summary>
/// <remarks>
///     The invoker overrides <c>CheckLoadedInvariants</c> by hand, in the shape the generator emits for
///     both kinds of action: what is measured is where the void pipeline asks it, and what it does with
///     the answer.
/// </remarks>
public sealed class AVoidActionRefusesARowThatBreaksItsRulesTests
{
    private sealed class Charge
    {
        public decimal Amount { get; set; }
    }

    private sealed class RefundCharge : VoidDomainAction
    {
        public Charge Loaded { get; } = new() { Amount = 10 };
        public decimal Refund { get; init; }

        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
        {
            Loaded.Amount -= Refund;
            return Task.FromResult(VoidResult<IError>.Success());
        }
    }

    private sealed class RefundChargeInvoker(IServiceProvider sp) : VoidDomainActionInvoker<RefundCharge>(sp)
    {
        public int Saves { get; private set; }

        protected override void InjectDependencies(RefundCharge action) { }

        protected override Task SaveChangesAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }

        // What the generator emits for an [Invariant] on the loaded entity.
        protected override IError? CheckLoadedInvariants(RefundCharge action)
            => action.Loaded.Amount >= 0
                ? null
                : new InvariantViolationError("AmountIsNotNegative", "Amount must not be negative");
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new ValidationOptions()));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ABodyThatBreaksTheRule_IsRefused_AndNothingIsSaved()
    {
        using var sp = Services();
        var invoker = new RefundChargeInvoker(sp);

        var result = await invoker.InvokeAsync(new RefundCharge { Refund = 15 });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvariantViolationError>();
        invoker.Saves.Should().Be(0, "the rule is checked before the write");
    }

    /// <summary>The control: a body that keeps the rule is saved.</summary>
    [Fact]
    public async Task ABodyThatKeepsTheRule_IsSaved()
    {
        using var sp = Services();
        var invoker = new RefundChargeInvoker(sp);

        var result = await invoker.InvokeAsync(new RefundCharge { Refund = 5 });

        result.IsSuccess.Should().BeTrue();
        invoker.Saves.Should().Be(1);
    }
}
