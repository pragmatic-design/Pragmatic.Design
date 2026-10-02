using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Mutation;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Invoking a mutation against an entity the caller already holds.
/// </summary>
/// <remarks>
///     <para>
///         The load is the only part of the pipeline a caller can already have done. An action that read
///         an entity to check something and then mutates it paid for two reads of the same row; a sweep
///         over a hundred rows paid a hundred and one. That cost is what made writing to the repository
///         by hand look reasonable — and a hand-written write carries no permission, no entity
///         validation and no entry in the processing register.
///     </para>
///     <para>
///         Everything else still runs, which is the point: this skips a round trip, not a pipeline.
///     </para>
/// </remarks>
public class MutationInvokerPreloadedEntityTests
{
    private sealed class Product : IChangeTracking
    {
        private readonly HashSet<string> _modified = [];

        public string Name { get; private set; } = "";
        public IReadOnlySet<string> ModifiedProperties => _modified;
        public IReadOnlySet<string> CollectionsModified { get; } = new HashSet<string>();
        public bool IsNew { get; set; }

        internal void SetName(string value)
        {
            Name = value;
            _modified.Add(nameof(Name));
        }

        public void ResetModifiedProperties() => _modified.Clear();
    }

    private sealed class RenameProduct : Mutation<Product>
    {
        public required string NewName { get; init; }

        public override Task<Result<Product, IError>> ApplyAsync(Product entity, CancellationToken ct = default)
        {
            entity.SetName(NewName);
            return Task.FromResult<Result<Product, IError>>(entity);
        }
    }

    private sealed class CreateProduct : Mutation<Product>
    {
        public required string Name { get; init; }

        public override Task<Result<Product, IError>> ApplyAsync(Product entity, CancellationToken ct = default)
        {
            entity.SetName(Name);
            return Task.FromResult<Result<Product, IError>>(entity);
        }
    }

    private sealed class RenameInvoker(IServiceProvider sp, MutationMode mode)
        : MutationInvoker<RenameProduct, Product>(sp)
    {
        public int Loads { get; private set; }
        public int Saves { get; private set; }
        public Product? Stored { get; set; }

        protected override void InjectDependencies(RenameProduct mutation) { }

        protected override Task<Product?> LoadEntityAsync(RenameProduct mutation, CancellationToken ct)
        {
            Loads++;
            return Task.FromResult(Stored);
        }

        protected override Product CreateEntity() => new();
        protected override MutationMode GetMode() => mode;
        protected override string? GetEntityIdString(RenameProduct mutation) => "test-id";
        protected override void PersistNew(Product entity) { }
        protected override void DeleteEntity(Product entity) { }

        protected override Task SaveChangesAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class CreateInvoker(IServiceProvider sp) : MutationInvoker<CreateProduct, Product>(sp)
    {
        protected override void InjectDependencies(CreateProduct mutation) { }

        protected override Task<Product?> LoadEntityAsync(CreateProduct mutation, CancellationToken ct)
            => Task.FromResult<Product?>(null);

        protected override Product CreateEntity() => new();
        protected override MutationMode GetMode() => MutationMode.Create;
        protected override string? GetEntityIdString(CreateProduct mutation) => null;
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

    [Fact]
    public async Task GivenTheEntity_TheInvokerDoesNotLoadItAgain()
    {
        using var provider = BuildProvider();
        var invoker = new RenameInvoker(provider, MutationMode.Update) { Stored = new Product() };
        var entity = new Product();
        entity.SetName("Original");
        entity.ResetModifiedProperties();

        var result = await invoker.InvokeAsync(new RenameProduct { NewName = "Renamed" }, entity);

        result.IsSuccess.Should().BeTrue();
        invoker.Loads.Should().Be(0, "the caller had already read the row");
    }

    [Fact]
    public async Task GivenTheEntity_ThatEntityIsTheOneMutated()
    {
        // Not merely "an entity was returned": the invoker holds a different instance, so mutating the
        // wrong one would still succeed and still return something.
        using var provider = BuildProvider();
        var stored = new Product();
        stored.SetName("Stored");
        var invoker = new RenameInvoker(provider, MutationMode.Update) { Stored = stored };

        var mine = new Product();
        mine.SetName("Mine");
        mine.ResetModifiedProperties();

        var result = await invoker.InvokeAsync(new RenameProduct { NewName = "Renamed" }, mine);

        result.Value.Should().BeSameAs(mine);
        mine.Name.Should().Be("Renamed");
        stored.Name.Should().Be("Stored", "the invoker's own row must not have been touched");
    }

    [Fact]
    public async Task WithoutTheEntity_TheInvokerStillLoads()
    {
        // The overload adds a way in; it does not change the one that was there.
        using var provider = BuildProvider();
        var invoker = new RenameInvoker(provider, MutationMode.Update) { Stored = new Product() };

        await invoker.InvokeAsync(new RenameProduct { NewName = "Renamed" });

        invoker.Loads.Should().Be(1);
    }

    [Fact]
    public async Task GivenTheEntity_TheRestOfThePipelineStillRuns()
    {
        // Skipping the load must not skip the save, or the mutation would report success on nothing.
        using var provider = BuildProvider();
        var invoker = new RenameInvoker(provider, MutationMode.Update) { Stored = new Product() };

        await invoker.InvokeAsync(new RenameProduct { NewName = "Renamed" }, new Product());

        invoker.Saves.Should().Be(1);
    }

    [Fact]
    public async Task GivenTheEntity_ChangeTrackingStartsFromTheHandover()
    {
        // The load path resets modified properties after reading. A handed-over entity has to get the
        // same treatment: without it, change-aware entity validation sees whatever the caller touched
        // before passing it, and validates the wrong set.
        using var provider = BuildProvider();
        var invoker = new RenameInvoker(provider, MutationMode.Update) { Stored = new Product() };

        var entity = new Product();
        entity.SetName("Set by the caller for its own reasons");

        await invoker.InvokeAsync(new RenameProduct { NewName = "Renamed" }, entity);

        entity.ModifiedProperties.Should().Contain(nameof(Product.Name));
        entity.ModifiedProperties.Should().HaveCount(1);
    }

    [Fact]
    public async Task ACreateMutationRefusesAnEntity()
    {
        // Creating is what that mode does. Quietly updating the given row instead would be the wrong
        // operation performed successfully.
        using var provider = BuildProvider();
        var invoker = new CreateInvoker(provider);

        var act = async () =>
            await invoker.InvokeAsync(new CreateProduct { Name = "New" }, new Product()).ConfigureAwait(false);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Create");
    }

    [Fact]
    public async Task ANullEntityIsRefusedBeforeAnythingRuns()
    {
        using var provider = BuildProvider();
        var invoker = new RenameInvoker(provider, MutationMode.Update);

        var act = async () =>
            await invoker.InvokeAsync(new RenameProduct { NewName = "x" }, null!).ConfigureAwait(false);

        await act.Should().ThrowAsync<ArgumentNullException>();
        invoker.Saves.Should().Be(0);
    }
}
