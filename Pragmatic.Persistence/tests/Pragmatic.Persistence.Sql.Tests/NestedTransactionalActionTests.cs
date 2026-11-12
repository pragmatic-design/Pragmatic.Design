using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Pragmatic.Result;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A <c>[Transactional]</c> action invoked inside another joins the outer transaction with a savepoint instead of opening a second.
///     Five databases.
/// </summary>
/// <remarks>
///     ⚠️ <c>SqlUnitOfWork.BeginTransactionAsync</c> called <c>SqlSession.BeginTransaction</c>, which refuses a transaction while one is open:
///     an action composing others in one transaction could not itself be composed, the inner invocation threw before its body ran. The same
///     three cases as <c>NestedTransactionalActionTests</c> on EF Core, with the invokers written by hand the way the generated ones are; the rows
///     are what is asserted.
/// </remarks>
[Collection("pragmatic databases")]
public class NestedTransactionalActionTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_transactional_action_invoking_another_succeeds_and_commits_both(SqlDialect dialect)
    {
        var written = await RunAsync(dialect, innerSucceeds: true, outerSucceeds: true);

        Assert.True(written.Result.IsSuccess, written.Result.IsFailure ? written.Result.Error.Code : null);
        Assert.Equal(["nested-inner", "nested-outer-after", "nested-outer-before"], written.Names);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_inner_failure_rolls_back_to_its_savepoint_and_the_outer_continues(SqlDialect dialect)
    {
        var written = await RunAsync(dialect, innerSucceeds: false, outerSucceeds: true);

        Assert.True(written.Result.IsSuccess, "the outer action goes on after the inner one failed");
        Assert.Equal(["nested-outer-after", "nested-outer-before"], written.Names); // the inner one's write is undone, and only it
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_outer_failure_rolls_back_both(SqlDialect dialect)
    {
        var written = await RunAsync(dialect, innerSucceeds: true, outerSucceeds: false);

        Assert.True(written.Result.IsFailure);
        Assert.Empty(written.Names); // the inner one committed into the outer transaction, which was rolled back
    }

    private async Task<(VoidResult<IError> Result, List<string> Names)> RunAsync(SqlDialect dialect, bool innerSucceeds, bool outerSucceeds)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var inner = new Invoker(services, unitOfWork);
        var outer = new Invoker(services, unitOfWork);

        var result = await outer.InvokeAsync(new WorkAction(async ct =>
        {
            await WriteAsync(unitOfWork, "nested-outer-before", ct);
            await inner.InvokeAsync(new WorkAction(async innerCt =>
            {
                await WriteAsync(unitOfWork, "nested-inner", innerCt);
                return innerSucceeds;
            }), ct);
            await WriteAsync(unitOfWork, "nested-outer-after", ct);
            return outerSucceeds;
        }));

        var names = (await UnitOfWorkTests.RawAsync<Property>(session, _toggle)).Select(p => p.Name).Where(n => n.StartsWith("nested-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToList();
        return (result, names);
    }

    private static async Task WriteAsync(SqlUnitOfWork unitOfWork, string name, CancellationToken ct)
    {
        new Property.SqlRepository(unitOfWork).Add(new Property { Name = name });
        await unitOfWork.SaveChangesAsync(ct);
    }

    private sealed record TestError(string Code) : IError
    {
        public int StatusCode => 500;
        public string Title => Code;
    }

    /// <summary>A body that writes, then says whether the action succeeded.</summary>
    private sealed class WorkAction(Func<CancellationToken, Task<bool>> body) : VoidDomainAction
    {
        public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            => await body(ct) ? VoidResult<IError>.Success() : VoidResult<IError>.Failure(new TestError("FAILED"));
    }

    /// <summary>Stands in for the generated invoker of a <c>[Transactional]</c> action.</summary>
    private sealed class Invoker(IServiceProvider services, IUnitOfWork unitOfWork) : VoidDomainActionInvoker<WorkAction>(services)
    {
        protected override void InjectDependencies(WorkAction action) { }

        protected override IUnitOfWork? UnitOfWork => unitOfWork;

        protected override bool IsTransactional => true;

        protected override Task SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);
    }
}
