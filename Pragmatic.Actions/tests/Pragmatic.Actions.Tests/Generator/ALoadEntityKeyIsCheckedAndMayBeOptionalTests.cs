using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     The key <c>[LoadEntity]</c> names is checked against the entity's key, and a nullable key is an
///     optional load.
/// </summary>
/// <remarks>
///     The key's type was never compared with the entity's, so a <c>string</c> key on a
///     <c>Guid</c>-keyed entity was CS1503 inside the generated invoker — and so was <c>Guid?</c>, the
///     natural "load it if given", which Time off's team update did by hand for exactly that reason.
/// </remarks>
public class ALoadEntityKeyIsCheckedAndMayBeOptionalTests : ActionsGeneratorTestBase
{
    private const string Header = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.People;

        public partial class Employee : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        public partial class Team : IEntity
        {
            public Guid PersistenceId { get; set; }
            public Guid? ManagerId { get; set; }
        }

        """;

    private const string Optional = Header + """
        [DomainAction]
        [LoadEntity<Employee>(nameof(ManagerId))]
        public partial class AssignManagerAction : DomainAction<bool>
        {
            public Guid? ManagerId { get; init; }

            public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<bool, IError>>(_employee is not null);
        }

        [Mutation(Mode = MutationMode.Update)]
        [LoadEntity<Employee>(nameof(ManagerId))]
        public partial class UpdateTeamMutation : Mutation<Team>
        {
            public required Guid Id { get; init; }

            public Guid? ManagerId { get; init; }
        }

        [DomainAction]
        [LoadEntity<Employee>(nameof(EmployeeId))]
        public partial class LookAction : DomainAction<bool>
        {
            public required Guid EmployeeId { get; init; }

            public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<bool, IError>>(true);
        }
        """;

    [Fact]
    public void ANullableKey_IsAnOptionalLoad()
    {
        var result = RunGeneratorWithEntities(Optional);

        var field = GetGeneratedSource(result, "AssignManagerAction.LoadEntity");
        field.Should().NotBeNull();
        field!.Should().Contain("global::TestApp.People.Employee? _employee");

        var preparation = GetGeneratedSource(result, "AssignManagerAction.Invoker")!;
        preparation.Should().Contain("if (action.ManagerId is { } ",
            "the row is read only when the key has a value");
    }

    /// <summary>The control: a key that is not nullable still answers 404 for a row that is not there.</summary>
    [Fact]
    public void ARequiredKey_StillAnswers404()
    {
        var result = RunGeneratorWithEntities(Optional);

        GetGeneratedSource(result, "LookAction.LoadEntity")!
            .Should().Contain("global::TestApp.People.Employee _employee");
        var invoker = GetGeneratedSource(result, "LookAction.Invoker")!;
        invoker.Should().Contain("GetByIdAsync(action.EmployeeId, ct)");
        invoker.Should().Contain("NotFoundError.For(\"Employee\"");
    }

    [Fact]
    public void TheOptionalLoads_Compile()
    {
        var result = RunGeneratorWithEntities(Optional);

        HasDiagnostic(result, "PRAG0411").Should().BeFalse("a Guid? key matches a Guid-keyed entity");
        GetCompilationErrors(result).Select(e => e.ToString()).Should().BeEmpty();
    }

    [Fact]
    public void AKeyOfAnotherType_IsReported()
    {
        var result = RunGeneratorWithEntities(Header + """
            [DomainAction]
            [LoadEntity<Employee>(nameof(EmployeeCode))]
            public partial class LookByCodeAction : DomainAction<bool>
            {
                public required string EmployeeCode { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        HasDiagnostic(result, "PRAG0411").Should().BeTrue();
        GetCompilationErrors(result).Where(e => e.Location.SourceTree?.FilePath.Contains(".g.cs") == true)
            .Select(e => e.ToString()).Should().BeEmpty("the operation is reported, not a file the author cannot open");
    }
}
