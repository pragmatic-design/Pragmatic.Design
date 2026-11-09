using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     A <c>[Transactional]</c> action invoked inside another joins the outer transaction instead of opening a second.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>EfCoreUnitOfWork.BeginTransactionAsync</c> began a database transaction whatever was open, and EF Core
///         refuses a second one on the same connection. An action that composes others in one transaction could not itself
///         be composed: the inner invocation threw before its body ran.
///     </para>
///     <para>
///         The invokers are written by hand, the way the generated ones are, over a real unit of work on SQLite, which has
///         savepoints. The rows are what is asserted, not the sequence of calls.
///     </para>
/// </remarks>
public sealed class NestedTransactionalActionTests : IDisposable
{
    private readonly TestDbContext _db;
    private readonly EfCoreUnitOfWork _unitOfWork;
    private readonly IServiceProvider _services;

    public NestedTransactionalActionTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite("DataSource=:memory:").Options;
        _db = new TestDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _unitOfWork = new EfCoreUnitOfWork(_db);

        var services = new ServiceCollection();
        services.AddLogging();
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task ATransactionalActionInvokingAnother_Succeeds_AndCommitsBoth()
    {
        var result = await Outer(innerSucceeds: true, outerSucceeds: true);

        result.IsSuccess.Should().BeTrue();
        StoredNames().Should().BeEquivalentTo(["outer-before", "inner", "outer-after"]);
    }

    [Fact]
    public async Task TheInnerFailure_RollsBackToItsSavepoint_AndTheOuterContinues()
    {
        var result = await Outer(innerSucceeds: false, outerSucceeds: true);

        result.IsSuccess.Should().BeTrue("the outer action goes on after the inner one failed");
        StoredNames().Should().BeEquivalentTo(["outer-before", "outer-after"],
            "the inner one's write is undone, and only it");
    }

    [Fact]
    public async Task TheOuterFailure_RollsBackBoth()
    {
        var result = await Outer(innerSucceeds: true, outerSucceeds: false);

        result.IsFailure.Should().BeTrue();
        StoredNames().Should().BeEmpty("the inner one committed into the outer transaction, which was rolled back");
    }

    private Task<VoidResult<IError>> Outer(bool innerSucceeds, bool outerSucceeds)
    {
        var inner = new Invoker<WorkAction>(_services, _unitOfWork);
        var outer = new Invoker<WorkAction>(_services, _unitOfWork);

        return outer.InvokeAsync(new WorkAction(async ct =>
        {
            await Write("outer-before", ct).ConfigureAwait(false);
            await inner.InvokeAsync(new WorkAction(async innerCt =>
            {
                await Write("inner", innerCt).ConfigureAwait(false);
                return innerSucceeds;
            }), ct).ConfigureAwait(false);
            await Write("outer-after", ct).ConfigureAwait(false);
            return outerSucceeds;
        }));
    }

    private async Task Write(string name, CancellationToken ct)
    {
        _db.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = name });
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private List<string> StoredNames()
    {
        _db.ChangeTracker.Clear();
        return [.. _db.Set<TestProduct>().AsNoTracking().Select(p => p.Name)];
    }

    public void Dispose() => _db.Dispose();

    private sealed record TestError(string Code) : IError
    {
        public int StatusCode => 500;
        public string Title => Code;
    }

    /// <summary>A body that writes, then says whether the action succeeded.</summary>
    private sealed class WorkAction(Func<CancellationToken, Task<bool>> body) : VoidDomainAction
    {
        public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            => await body(ct).ConfigureAwait(false) ? VoidResult<IError>.Success() : VoidResult<IError>.Failure(new TestError("FAILED"));
    }

    /// <summary>Stands in for the generated invoker of a <c>[Transactional]</c> action.</summary>
    private sealed class Invoker<TAction>(IServiceProvider services, IUnitOfWork unitOfWork)
        : VoidDomainActionInvoker<TAction>(services)
        where TAction : VoidDomainAction
    {
        protected override void InjectDependencies(TAction action) { }

        protected override IUnitOfWork? UnitOfWork => unitOfWork;

        protected override bool IsTransactional => true;

        protected override Task SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);
    }
}
