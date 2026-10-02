using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Mutation;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     An id the caller sent is a reference to a row, not a suggestion.
/// </summary>
/// <remarks>
///     <para>
///         <c>CreateOrUpdate</c> read its own name as "load, and if you do not find it, create": the
///         fallback ran whenever the mode carried <c>Create</c>, without asking whether the caller had
///         supplied an id at all. So an invented id answered <b>200</b> and wrote a new row — with an
///         id <em>different</em> from the one sent, because the fallback builds a fresh entity. An id
///         wrong by one letter, or a row retired since the client last read it, lost the refinement
///         and fabricated a copy in silence.
///     </para>
///     <para>
///         Creating with a different id than the one asked for is nobody's intended semantics, upsert
///         included, which is what makes this a defect rather than a choice between two readings. The
///         rule: creation is the fallback of a <b>missing id</b>, not of a failed load.
///     </para>
/// </remarks>
public class CreateOrUpdateWithAnUnknownIdTests
{
    private sealed class Product
    {
        public Guid Id { get; init; }
        public string Name { get; private set; } = "";

        internal void SetName(string value) => Name = value;
    }

    private sealed class UpsertProduct : Mutation<Product>
    {
        public Guid Id { get; init; }
        public required string Name { get; init; }

        public override Task<Result<Product, IError>> ApplyAsync(Product entity, CancellationToken ct = default)
        {
            entity.SetName(Name);
            return Task.FromResult<Result<Product, IError>>(entity);
        }
    }

    /// <summary>An invoker whose store is empty: every load misses.</summary>
    private sealed class UpsertInvoker(IServiceProvider sp) : MutationInvoker<UpsertProduct, Product>(sp)
    {
        public int Created { get; private set; }

        protected override void InjectDependencies(UpsertProduct mutation) { }

        protected override Task<Product?> LoadEntityAsync(UpsertProduct mutation, CancellationToken ct)
            => Task.FromResult<Product?>(null);

        protected override Product CreateEntity()
        {
            Created++;
            return new Product { Id = Guid.NewGuid() };
        }

        protected override MutationMode GetMode() => MutationMode.CreateOrUpdate;
        protected override string? GetEntityIdString(UpsertProduct mutation) => mutation.Id.ToString();
        protected override bool HasEntityId(UpsertProduct mutation) => mutation.Id != default;
        protected override void PersistNew(Product entity) { }
        protected override void DeleteEntity(Product entity) { }
        protected override Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new ValidationOptions()));

        return services.BuildServiceProvider();
    }

    /// <summary>An id that names nothing is refused, not turned into a new row.</summary>
    [Fact]
    public async Task AnIdThatNamesNothing_IsRefused()
    {
        using var provider = BuildProvider();
        var invoker = new UpsertInvoker(provider);
        var invented = Guid.NewGuid();

        var result = await invoker.InvokeAsync(new UpsertProduct { Id = invented, Name = "Renamed" });

        result.IsSuccess.Should().BeFalse(
            "the caller named a row; naming one that is not there is a mistake worth telling them about");
        invoker.Created.Should().Be(0, "and nothing was fabricated in its place");
    }

    /// <summary>
    ///     The control: with no id supplied, <c>CreateOrUpdate</c> still creates.
    /// </summary>
    /// <remarks>
    ///     Without it, "an unknown id is refused" is satisfied by removing the create fallback
    ///     altogether, which turns <c>CreateOrUpdate</c> into <c>Update</c> and takes the mode's
    ///     reason for existing with it.
    /// </remarks>
    [Fact]
    public async Task WithNoIdSupplied_ItStillCreates()
    {
        using var provider = BuildProvider();
        var invoker = new UpsertInvoker(provider);

        var result = await invoker.InvokeAsync(new UpsertProduct { Name = "Fresh" });

        result.IsSuccess.Should().BeTrue("no id was sent, so there was no row to refine");
        invoker.Created.Should().Be(1);
        result.Value!.Name.Should().Be("Fresh");
    }

    /// <summary>
    ///     The second control: an id that does name a row still refines it, without creating.
    /// </summary>
    /// <remarks>
    ///     This is the half the defect destroyed — the refinement — and asserting it here is what keeps
    ///     the refusal from being applied to the case that was working.
    /// </remarks>
    [Fact]
    public async Task AnIdThatNamesARow_StillRefinesIt()
    {
        using var provider = BuildProvider();
        var existing = new Product { Id = Guid.NewGuid() };
        existing.SetName("Original");
        var invoker = new FoundInvoker(provider, existing);

        var result = await invoker.InvokeAsync(new UpsertProduct { Id = existing.Id, Name = "Refined" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(existing);
        existing.Name.Should().Be("Refined");
        invoker.Created.Should().Be(0);
    }

    /// <summary>The same invoker, with a row in the store.</summary>
    private sealed class FoundInvoker(IServiceProvider sp, Product stored)
        : MutationInvoker<UpsertProduct, Product>(sp)
    {
        public int Created { get; private set; }

        protected override void InjectDependencies(UpsertProduct mutation) { }

        protected override Task<Product?> LoadEntityAsync(UpsertProduct mutation, CancellationToken ct)
            => Task.FromResult<Product?>(mutation.Id == stored.Id ? stored : null);

        protected override Product CreateEntity()
        {
            Created++;
            return new Product { Id = Guid.NewGuid() };
        }

        protected override MutationMode GetMode() => MutationMode.CreateOrUpdate;
        protected override string? GetEntityIdString(UpsertProduct mutation) => mutation.Id.ToString();
        protected override bool HasEntityId(UpsertProduct mutation) => mutation.Id != default;
        protected override void PersistNew(Product entity) { }
        protected override void DeleteEntity(Product entity) { }
        protected override Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
