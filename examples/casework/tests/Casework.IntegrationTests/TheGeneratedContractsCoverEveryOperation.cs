using Pragmatic.Testing;
using Pragmatic.Testing.Assertions;
using Pragmatic.Tests.Generated;

namespace Casework.IntegrationTests;

/// <summary>
///     Every operation these two services publish has a contract, and the ones that get less
///     than the full set say which part they do not get.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>A generated suite reports what it wrote and never what it skipped.</b> An operation with
///         no contract at all is invisible among the generated names, and finding it by comparing them
///         against the route table by hand is slow and easy to misread.
///     </para>
///     <para>
///         So the first test here reads the generator's coverage report instead, and the second is a
///         <b>pinned absence</b>: the four operations the generator declines are named, so a fifth
///         joining them fails this test instead of joining the silence.
///     </para>
/// </remarks>
public sealed class TheGeneratedContractsCoverEveryOperation
{
    /// <summary>Every published operation got at least one contract.</summary>
    /// <remarks>
    ///     The usual cause of an uncovered operation is an authorization contract the generator cannot
    ///     emit: an operation with no <c>[RequirePermission]</c> has no caller to refuse, so there is no
    ///     difference to measure. The cure is on the application's side — a permission a role grants
    ///     has to be demanded by the operation it is for.
    /// </remarks>
    [Fact]
    public void NoOperationIsLeftWithoutAContract()
        => ContractCoverage.Uncovered.Select(operation => $"{operation.HttpMethod} {operation.Route}")
            .Should().BeEmpty();

    /// <summary>
    ///     And the ones that get an authorization contract and nothing else are these four, each for a
    ///     reason the generator states.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The names are pinned and the reasons are only required to exist: asserting the generator's
    ///     wording here would make this test a copy of its output, which fails on a reworded sentence and
    ///     passes on a missing contract. What matters is <b>which</b> operations are in the list.
    /// </remarks>
    [Fact]
    public void TheOperationsTheGeneratorDeclines_AreTheseFour_AndEachSaysWhy()
    {
        var declined = ContractCoverage.Operations
            .Where(operation => operation.NotCovered.Count > 0)
            .ToList();

        declined.Select(operation => operation.Operation).Should().BeEquivalentTo(new[]
        {
            // A POST on an existing case: the verification is the case moving, not a resource of its own.
            "AskForVerificationMutation",
            // A multipart upload, and a JSON body against it answers 415.
            "UploadALetterTemplateAction",
            "UploadCaseDocumentAction",
            // Onboarding writes a row in the tenant register, which is not an entity of this module.
            "OnboardAnOrganisationAction",
            // Verify's answer is a transition whose entity no route creates — the generator's own skip.
            "AnswerVerificationAction",
        });

        declined.Should().OnlyContain(operation => operation.NotCovered.All(reason => reason.Length > 0));
    }

    /// <summary>
    ///     The control: the four above are not the whole application, and the rest really are contracted.
    /// </summary>
    /// <remarks>
    ///     Without it, "the declined ones are these four" would also be satisfied by an application where
    ///     every operation is declined for something and the list simply happens to have four names in
    ///     it — and by one where the report is empty.
    /// </remarks>
    [Fact]
    public void TheIsolationContract_ExistsForTheAggregateAnyoneCanCreate()
    {
        var openACase = ContractCoverage.Operations
            .Should().ContainSingle(operation => operation.Operation == "OpenCaseMutation").Subject;

        openACase.Contracts.Should().BeEquivalentTo(new[] { "auth", "create", "validation", "isolation" });
    }
}
