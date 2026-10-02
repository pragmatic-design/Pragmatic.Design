using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     The field <c>[LoadCurrentUser]</c> promises, on the partial action or mutation, and the setter its
///     invoker hands the signed-in user's entity through.
/// </summary>
/// <remarks>
///     Beside <see cref="LoadEntityTemplate" /> rather than in it: that one hands over the entities its
///     keys name, in one call, and an operation may declare either without the other.
/// </remarks>
internal sealed class LoadCurrentUserTemplate(
    string typeName, string @namespace, string accessibility, CurrentUserLoadModel load) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{typeName} from {@namespace}";
    protected override string? TriggerInfo => $"[LoadCurrentUser] on {typeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(typeName, "LoadCurrentUser", @namespace),
        ToSourceText());

    protected override bool Validate() => load.IsRendered;

    public override void RenderFile()
    {
        AppendNamespace(@namespace);
        AppendLine();

        Class(typeName, RenderClassBody,
            accessModifier: TemplateHelpers.ParseAccessibility(accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderClassBody()
    {
        XmlSummary($"The signed-in user's <c>{load.UserTypeName}</c>. Never null once the invoker has prepared the operation: an account with none is answered 404, a caller who is not signed in 401.");
        Field(load.FieldName, load.UserTypeFullName!, AccessModifier.Private, initializer: "null!");
        AppendLine();

        XmlSummary("Sets the signed-in user's entity.");
        Method("SetCurrentUser", () => AppendLine($"{load.FieldName} = user;"), "void",
            [new MethodParameter(load.UserTypeFullName!, "user")], AccessModifier.Internal);
    }
}
