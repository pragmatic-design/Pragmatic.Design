using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Two <c>[LoadEntity]</c> of the same entity on one operation are read in one query — <c>WHERE Id IN (…)</c> —
///     and each field is taken from it, a missing key still a 404 naming it.
/// </summary>
/// <remarks>
///     The preparation names its local and the setter's parameter after the field, not the entity:
///     named after the entity, two loads of the same type would both be <c>employee</c> and not compile
///     (CS0128, CS0100).
/// </remarks>
public class LoadsOfOneEntityAreReadInOneQueryTests : ActionsGeneratorTestBase
{
    private const string Header = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.People;

        public partial class Employee : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Name { get; set; } = "";
            public Team? Team { get; set; }
        }

        public partial class Team : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        """;

    private static string Action(string loads, string properties = """
        public required Guid ManagerId { get; init; }
        public required Guid DeputyId { get; init; }
        """) => Header + $$"""
        [DomainAction]
        {{loads}}
        public partial class PairAction : DomainAction<string>
        {
            {{properties}}

            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<string, IError>>("");
        }
        """;

    private static string Invoker(Pragmatic.SourceGen.Testing.SourceGenRunResult result)
    {
        var errors = GetCompilationErrors(result).Select(d => d.ToString()).ToList();
        errors.Should().BeEmpty(string.Join(" | ", errors));
        return GetGeneratedSource(result, "PairAction.Invoker")!;
    }

    [Fact]
    public void TwoLoadsOfOneEntity_AreOneQuery_AndEachFieldIsTakenFromIt()
    {
        var invoker = Invoker(RunGeneratorWithEntities(Action("""
            [LoadEntity<Employee>(nameof(ManagerId), FieldName = "_manager")]
            [LoadEntity<Employee>(nameof(DeputyId), FieldName = "_deputy")]
            """)));

        invoker.Should().NotContain("GetByIdAsync", "the two keys are read together");
        invoker.Split("FindAsync(").Length.Should().Be(2, "one query for both");
        invoker.Should().Contain("global::Pragmatic.Result.Http.NotFoundError.For(\"Employee\", action.ManagerId.ToString())");
        invoker.Should().Contain("global::Pragmatic.Result.Http.NotFoundError.For(\"Employee\", action.DeputyId.ToString())");
        invoker.Should().Contain("action.SetLoadedEntities(manager, deputy);");
    }

    [Fact]
    public void AnOptionalKeyThatIsNull_IsNotAsked()
    {
        var invoker = Invoker(RunGeneratorWithEntities(Action("""
            [LoadEntity<Employee>(nameof(ManagerId), FieldName = "_manager")]
            [LoadEntity<Employee>(nameof(DeputyId), FieldName = "_deputy")]
            """, """
            public required Guid ManagerId { get; init; }
            public Guid? DeputyId { get; init; }
            """)));

        invoker.Should().Contain("if (action.DeputyId is { } __deputyKey)");
        invoker.Split("FindAsync(").Length.Should().Be(2);
    }

    /// <summary>Two <c>[LoadEntities]</c> of one entity compile too: their names come from the field as well.</summary>
    [Fact]
    public void TwoListLoadsOfOneEntity_Compile()
    {
        Invoker(RunGeneratorWithEntities(Action("""
            [LoadEntities<Employee>(nameof(ManagerIds), FieldName = "_managers")]
            [LoadEntities<Employee>(nameof(DeputyIds), FieldName = "_deputies")]
            """, """
            public required IReadOnlyList<Guid> ManagerIds { get; init; }
            public required IReadOnlyList<Guid> DeputyIds { get; init; }
            """))).Should().Contain("action.SetLoadedEntities(managers, deputies);");
    }

    /// <summary>Different <c>Include</c> paths are different reads: not merged.</summary>
    [Fact]
    public void LoadsWithDifferentIncludes_AreReadApart()
    {
        var invoker = Invoker(RunGeneratorWithEntities(Action("""
            [LoadEntity<Employee>(nameof(ManagerId), FieldName = "_manager", Include = "Team")]
            [LoadEntity<Employee>(nameof(DeputyId), FieldName = "_deputy")]
            """)));

        invoker.Should().Contain("\"Team\"");
        invoker.Should().Contain("GetByIdAsync(action.DeputyId, ct)");
    }

    /// <summary>The control: one load is read by its key, as before.</summary>
    [Fact]
    public void ASingleLoad_IsReadByItsKey()
    {
        var invoker = Invoker(RunGeneratorWithEntities(Action(
            "[LoadEntity<Employee>(nameof(ManagerId))]",
            "public required Guid ManagerId { get; init; }")));

        invoker.Should().Contain("_employeeRepository.GetByIdAsync(action.ManagerId, ct)");
        invoker.Should().NotContain("FindAsync(");
    }
}
