using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.Testing.SourceGenerator.Models;
using Pragmatic.Testing.SourceGenerator.Transforms;

namespace Pragmatic.Testing.SourceGenerator.Templates;

/// <summary>
///     Emits state-transition contract tests for a boundary (#7): per the doc, "POST the transition endpoint →
///     status changed; illegal transition → 409". Each transition gets both halves: the move from the initial
///     state, and the opposite answer from a state reached by walking the entity's other transitions
///     (<see cref="TransitionPathFinder" />).
/// </summary>
/// <remarks>
///     The second half is not a <c>[Fact(Skip = …)]</c> placeholder: the flow is in the declarations, so it is
///     walked; an entity with no create route is arranged by the application
///     (<c>PragmaticContractHost.ArrangeFor</c>). A walk that does not exist is no test at all, and the
///     generator reports it (PRAG2363) instead of emitting one that could only be skipped.
/// </remarks>
internal sealed class StateTransitionTestTemplate : CSharpTemplate
{
    private const string Identity = "global::Pragmatic.Testing.PragmaticTestIdentity";
    private const string Assert = "global::Pragmatic.Testing.PragmaticHttpAssertions";
    private const string Json = "global::System.Net.Http.Json.JsonContent";
    private const string Host = "global::Pragmatic.Testing.PragmaticContractHost";
    private const string Fact = "global::Xunit.FactAttribute";
    private const string Task = "global::System.Threading.Tasks.Task";
    private const string Request = "global::System.Net.Http.HttpRequestMessage";
    private const string Verb = "global::System.Net.Http.HttpMethod";

    private readonly string _boundary;
    private readonly IReadOnlyList<StateTransitionModel> _transitions;

    public StateTransitionTestTemplate(string boundary, IReadOnlyList<StateTransitionModel> transitions)
    {
        _boundary = boundary;
        _transitions = transitions;
    }

    protected override string? GeneratorName => "Pragmatic.Testing.SourceGenerator";

    public override Artifact RenderOutput() => new($"_ContractTests.{_boundary}.Transitions.g.cs", ToSourceText());

    protected override bool Validate() => _transitions.Count > 0;

    public override void RenderFile()
    {
        AppendLine("namespace Pragmatic.Tests.Generated;");
        AppendLine();
        AppendLine("[global::Xunit.CollectionAttribute(\"PragmaticContractTests\")]");
        AppendLine($"public sealed partial class {_boundary}TransitionContractTests : global::Pragmatic.Testing.PragmaticContractTestBase");
        AppendLine("{");
        IncreaseIndent();

        // Which host answers these: the boundary is in this class's own name, so the generator states it
        // instead of leaving a multi-service application to route by route prefix.
        AppendLine($"protected override string Boundary => \"{_boundary}\";");

        foreach (var transition in _transitions)
        {
            var ofTheEntity = _transitions.Where(t => t.EntityName == transition.EntityName).ToList();

            if (transition.InitialToTargetIsLegal)
            {
                RenderContract(transition, "FromInitial_Succeeds", [], succeeds: true);

                if (TransitionPathFinder.ToAnIllegalSource(transition, ofTheEntity) is { } walk)
                    RenderContract(transition, "FromIllegalState_IsRejectedWithConflict", walk, succeeds: false);
            }
            else
            {
                RenderContract(transition, "FromInitial_IsRejectedWithConflict", [], succeeds: false);

                if (TransitionPathFinder.ToALegalSource(transition, ofTheEntity) is { } walk)
                    RenderContract(transition, "FromLegalState_Succeeds", walk, succeeds: true);
            }
        }

        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     One contract: bring the entity into its initial state, walk <paramref name="walk" />, then POST
    ///     the transition and expect 2xx or 409.
    /// </summary>
    private void RenderContract(
        StateTransitionModel t, string suffix, IReadOnlyList<StateTransitionModel> walk, bool succeeds)
    {
        AppendLine();
        AppendLine($"[{Fact}]");
        AppendLine($"public async {Task} {t.EntityName}_TransitionTo{t.TargetState}_{suffix}()");
        AppendLine("{");
        IncreaseIndent();

        // ⚠️ One person, every request. The entity is usually visible to whoever created it, so a create and
        // a transition made by two identities end in a 404 on a row that exists — measured in the Showcase,
        // where the fixture had to substitute an id of its own to get past it. Each request still carries
        // its own endpoint's permission: they are gated separately.
        var caller = $"contract-{ToKebab(t.EntityName)}-flow";

        RenderArrange(t, caller);

        for (var i = 0; i < walk.Count; i++)
        {
            var prefix = $"step{i + 1}";
            RenderPost(walk[i], prefix, caller);
            AppendLine($"{Assert}.ShouldBeSuccess({prefix}Response);");
        }

        RenderPost(t, "transition", caller);
        AppendLine(succeeds
            ? $"{Assert}.ShouldBeSuccess(transitionResponse);"
            : $"{Assert}.ShouldBeConflict(transitionResponse);");

        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>Declares <c>id</c>: an entity in its initial state, created over HTTP or arranged by the application.</summary>
    private void RenderArrange(StateTransitionModel t, string caller)
    {
        // No create route: the entity comes into being as a consequence of something else, and only the
        // application knows what. It says so through ArrangeFor, and the test fails naming it when it does not.
        if (t.UngeneratableReason is { Length: > 0 })
        {
            AppendLine($"var id = await {Host}.ArrangeAsync(\"{t.EntityName}\", Client);");
            return;
        }

        // ⚠️ Named, and an array: AsUser takes string[]. A bare string in the fourth position bound to no
        // overload, so the first transition contract that stopped being a placeholder failed to compile.
        AppendLine("var body = new");
        AppendLine("{");
        IncreaseIndent();
        foreach (var field in t.CreateFields)
            AppendLine($"{field.Name} = {field.ValueExpression},");
        DecreaseIndent();
        AppendLine("};");

        AppendLine($"using var createRequest = new {Request}({Verb}.Post, \"{t.CreateRoute}\");");
        AppendLine($"{Identity}.AsUser(createRequest, {Caller(caller, t.CreatePermission)});");
        AppendLine($"{Host}.Prepare(createRequest, \"{t.CreateOperation}\", Boundary);");
        // ⚠️ Arranging the initial state means creating the entity, and a create whose validity needs
        // more than its shape — a foreign key to a row that must exist — cannot be synthesised. The
        // application supplies that body under the create operation's own name, which is the one its
        // CRUD contract asks under — not "Create" + the entity's, which was a second key for one
        // endpoint and made an application write the same body twice.
        AppendLine($"createRequest.Content = {Json}.Create({Host}.Body(\"{t.CreateOperation}\", body));");
        AppendLine("var createResponse = await Client.SendAsync(createRequest);");
        AppendLine($"{Assert}.ShouldBeSuccess(createResponse);");

        // ⚠️ Not "return when there is no location": that is a contract which asserts nothing and passes.
        AppendLine($"var id = await {Assert}.ShouldIdentifyTheCreatedAsync(createResponse);");
    }

    /// <summary>POSTs <paramref name="t" />'s route for <c>id</c>, as <c>{prefix}Request</c> / <c>{prefix}Response</c>.</summary>
    private void RenderPost(StateTransitionModel t, string prefix, string caller)
    {
        var operation = $"{t.EntityName}_TransitionTo{t.TargetState}";

        AppendLine($"var {prefix}Url = \"{t.TransitionRoute}\".Replace(\"{{id}}\", id);");
        AppendLine($"using var {prefix}Request = new {Request}({Verb}.Post, {prefix}Url);");
        AppendLine($"{Identity}.AsUser({prefix}Request, {Caller(caller, t.Permission)});");
        AppendLine($"{Host}.Prepare({prefix}Request, \"{operation}\", Boundary);");
        RenderTransitionBody(t, operation, prefix);
        AppendLine($"var {prefix}Response = await Client.SendAsync({prefix}Request);");
    }

    /// <summary>
    ///     The body the transition itself posts, when it takes one.
    /// </summary>
    /// <remarks>
    ///     ⚠️ An endpoint that requires a body answers <b>415</b> to a request with no content, before it
    ///     can answer the 409 or the 2xx the contract measures — so the assertion described the media
    ///     type and not the state machine. The body goes out under the transition's own operation name,
    ///     which is how an application replaces it through <c>BodyFor</c>: the create's name would hand
    ///     one operation's body to another.
    /// </remarks>
    private void RenderTransitionBody(StateTransitionModel t, string operation, string prefix)
    {
        if (!t.TransitionTakesABody)
            return;

        if (t.CanSynthesizeTransitionBody)
        {
            AppendLine($"var {prefix}Body = new");
            AppendLine("{");
            IncreaseIndent();
            foreach (var field in t.TransitionFields)
                AppendLine($"{field.Name} = {field.ValueExpression},");
            DecreaseIndent();
            AppendLine("};");
        }
        else
        {
            // Null, not an empty object: Body throws naming this operation and BodyFor, which is a
            // failure that says what to do. A `{}` would be a 400 read as a broken endpoint.
            AppendLine($"object? {prefix}Body = null;");
        }

        AppendLine($"{prefix}Request.Content = {Json}.Create({Host}.Body(\"{operation}\", {prefix}Body));");
    }

    /// <summary>
    ///     The identity arguments for one request of the flow: the flow's caller, carrying the
    ///     permission that request's own endpoint declares — or nothing, when it declares none.
    /// </summary>
    private static string Caller(string name, string? permission)
        => permission is null
            ? $"\"{name}\""
            : $"\"{name}\", permissions: [\"{permission}\"]";

    /// <summary>The entity name as it reads in an identity: <c>StoryTemplate</c> → <c>story-template</c>.</summary>
    private static string ToKebab(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}
