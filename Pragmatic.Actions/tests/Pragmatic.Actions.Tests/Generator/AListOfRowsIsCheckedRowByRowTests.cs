using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Mutation;
using Pragmatic.Result;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     A <c>[DomainAction]</c> over a <c>[LoadEntities&lt;T&gt;]</c> list asks each row's <c>[Invariant]</c> rules after the body and before
///     the write, and refuses with the first row that breaks one, naming it (#625).
/// </summary>
/// <remarks>
///     <para>
///         The generated <c>CheckLoadedInvariants</c> skipped a list: "a rule of the entity is about one row". So a bulk action wrote rows
///         no rule was asked of, while the same change through a single-row load was refused.
///     </para>
///     <para>
///         Measured on the generated invoker itself: the module is compiled, loaded and invoked over three rows, through a repository that
///         answers them. A refusal before the write is the pipeline's, which asks <c>CheckLoadedInvariants</c> before
///         <c>SaveChangesAsync</c>; what changed is what the generated override answers.
///     </para>
/// </remarks>
public class AListOfRowsIsCheckedRowByRowTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Interfaces;
        using Pragmatic.Persistence.Query.Results;
        using Pragmatic.Persistence.Repository;
        using Pragmatic.Result;
        using Pragmatic.Specification;

        namespace TestApp.Payroll;

        public partial class Employee : IEntity
        {
            public Guid PersistenceId { get; set; }
            public decimal Salary { get; set; }

            [Invariant("Salary must not be negative")]
            internal bool SalaryIsNotNegative() => Salary >= 0;
        }

        [DomainAction]
        [LoadEntities<Employee>(nameof(EmployeeIds))]
        public partial class CutSalariesAction : DomainAction<int>
        {
            public required IReadOnlyList<Guid> EmployeeIds { get; init; }
            public decimal Cut { get; init; }

            public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
            {
                foreach (var employee in _employees)
                    employee.Salary -= Cut;
                return Task.FromResult<Result<int, IError>>(_employees.Count);
            }
        }

        public sealed class Rows(List<Employee> rows) : IReadRepository<Employee>
        {
            public Task<List<Employee>> FindAsync(ISpecification<Employee> spec, CancellationToken ct = default)
                => Task.FromResult(rows.ToList());

            public Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<int> CountAsync(ISpecification<Employee> spec, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<bool> ExistsAsync(ISpecification<Employee> spec, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<Employee?> FirstOrDefaultAsync(ISpecification<Employee> spec, CancellationToken ct = default) => throw new NotSupportedException();
            public IQueryable<Employee> Query() => throw new NotSupportedException();
            public Task<IReadOnlyList<TResult>> RunAsync<TResult>(IQuery<Employee, TResult> query, CancellationToken ct = default)
                where TResult : class => throw new NotSupportedException();
            public Task<PagedResult<TResult>> RunAsync<TResult>(IPagedQuery<Employee, TResult> query, CancellationToken ct = default)
                where TResult : class => throw new NotSupportedException();
        }

        public static class Harness
        {
            /// <summary>Three rows with these salaries, every one cut by <paramref name="cut" /> through the generated invoker.</summary>
            public static async Task<(IError? Error, Guid[] Keys)> CutAsync(IServiceProvider services, decimal[] salaries, decimal cut)
            {
                var rows = salaries.Select(salary => new Employee { PersistenceId = Guid.NewGuid(), Salary = salary }).ToList();
                var invoker = new CutSalariesAction.Invoker(new Rows(rows), services);
                var result = await invoker.InvokeAsync(new CutSalariesAction
                {
                    EmployeeIds = rows.Select(row => row.PersistenceId).ToList(),
                    Cut = cut
                });
                return (result.IsFailure ? result.Error : null, rows.Select(row => row.PersistenceId).ToArray());
            }
        }
        """;

    [Fact]
    public async Task ARowThatBreaksTheRule_IsRefused_NamingTheRow()
    {
        var (error, keys) = await CutAsync([100m, 5m, 50m], cut: 10m);

        var refusal = error.Should().BeOfType<InvariantViolationError>().Subject;
        refusal.Invariant.Should().Be("SalaryIsNotNegative");
        refusal.EntityType.Should().Be("Employee");
        refusal.EntityId.Should().Be(keys[1].ToString(), "the second row is the one the cut takes below zero");
    }

    /// <summary>Two rows break it: the refusal is the first, in the order the keys were given.</summary>
    [Fact]
    public async Task TwoRowsThatBreakTheRule_TheFirstIsNamed()
    {
        var (error, keys) = await CutAsync([100m, 5m, 1m], cut: 10m);

        error.Should().BeOfType<InvariantViolationError>().Subject.EntityId.Should().Be(keys[1].ToString());
    }

    /// <summary>The control: rows that keep the rule are written, so the check refuses only what it should.</summary>
    [Fact]
    public async Task RowsThatKeepTheRule_AreAccepted()
    {
        var (error, _) = await CutAsync([100m, 15m, 50m], cut: 10m);

        error.Should().BeNull();
    }

    [Fact]
    public void TheEmittedCheck_AsksEachRow()
    {
        var invoker = GetGeneratedSource(RunGenerator(), "CutSalariesAction.Invoker")!;

        invoker.Should().Contain("foreach (var __row in action._employees)");
        invoker.Should().Contain("if (!__row.SalaryIsNotNegative())");
        invoker.Should().Contain("EntityType = \"Employee\", EntityId = __row.PersistenceId.ToString()");
    }

    private static SourceGenRunResult RunGenerator()
        => GeneratorTestHelper.RunGenerator<Pragmatic.SourceGenerator.PragmaticSourceGenerator>(Source, GetActionsAndEntityReferences());

    private static async Task<(IError? Error, Guid[] Keys)> CutAsync(decimal[] salaries, decimal cut)
    {
        var result = RunGenerator();
        GetCompilationErrors(result).Select(e => e.ToString()).Should().BeEmpty();

        using var image = new MemoryStream();
        var emitted = result.OutputCompilation.Emit(image);
        emitted.Success.Should().BeTrue(string.Join("\n", emitted.Diagnostics));

        var loadContext = new AssemblyLoadContext("load-entities-invariants", isCollectible: true);
        try
        {
            image.Position = 0;
            var harness = loadContext.LoadFromStream(image).GetType("TestApp.Payroll.Harness", throwOnError: true)!;

            var services = new ServiceCollection();
            services.AddSingleton(Options.Create(new ValidationOptions()));
            using var provider = services.BuildServiceProvider();

            var run = (Task<(IError?, Guid[])>)harness.GetMethod("CutAsync", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [provider, salaries, cut])!;
            return await run.ConfigureAwait(false);
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
