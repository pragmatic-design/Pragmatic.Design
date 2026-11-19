using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Result.Models;

namespace Pragmatic.SourceGenerator.Features.Result.Templates;

/// <summary>
///     The <c>IError</c> members of a C# 15 union error set, each delegated to the active case (#865).
/// </summary>
/// <remarks>
///     <para>
///         A union is a <c>TError</c> once it implements <c>IError</c>: <c>Result&lt;T, TError&gt;</c> needs nothing else. Its status, code
///         and title are those of the case it holds, so an operation that returns <c>NotFoundError</c> through the union answers 404, as
///         it does with the arity.
///     </para>
///     <para>
///         Every member is written, the defaulted ones too: a union that kept <c>IError</c>'s default <c>Title</c> would answer with an
///         empty title whatever the case says. A member the user wrote is left to the user.
///     </para>
/// </remarks>
internal sealed class UnionErrorTemplate : CSharpTemplate
{
    private const string Error = "global::Pragmatic.Result.IError";
    private const string Extensions = "global::System.Collections.Generic.IDictionary<string, object?>";
    private const string Resolver = "global::Pragmatic.Result.IErrorMessageResolver?";

    private readonly UnionErrorModel _model;

    public UnionErrorTemplate(UnionErrorModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Result";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"IError on the union {_model.TypeName}";

    protected override bool Validate() => _model.IsEmitted;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TypeName, "UnionError", _model.Namespace), ToSourceText());

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace))
            AppendNamespace(_model.Namespace);

        var accessibility = _model.Accessibility == "internal" ? AccessModifier.Internal : AccessModifier.Public;
        var modifiers = new ClassModifiers { Partial = true };
        if (_model.IsUnionDeclaration)
            Union(_model.TypeName, RenderBody, accessibility, modifiers);
        else
            Struct(_model.TypeName, RenderBody, accessModifier: accessibility, modifiers: modifiers);
    }

    private void RenderBody()
    {
        XmlSummary($"The case this {_model.TypeName} holds, as the error every IError member is delegated to.");
        AppendLine($"private {Error} PragmaticActiveCase => Value as {Error}");
        AppendLine($"    ?? throw new global::System.InvalidOperationException(\"This {_model.TypeName} holds no case: it was created " +
                   "with default, not from one of its errors.\");");
        AppendLine();

        var missing = _model.Missing;
        if (Has(missing, UnionErrorMembers.Code))
            Member("The code of the case this union holds.", "public string Code => PragmaticActiveCase.Code;");
        if (Has(missing, UnionErrorMembers.StatusCode))
            Member("The HTTP status of the case this union holds.", "public int StatusCode => PragmaticActiveCase.StatusCode;");
        if (Has(missing, UnionErrorMembers.Title))
            Member("The title of the case this union holds.", "public string Title => PragmaticActiveCase.Title;");
        if (Has(missing, UnionErrorMembers.Description))
            Member("The description of the case this union holds.", "public string? Description => PragmaticActiveCase.Description;");
        if (Has(missing, UnionErrorMembers.WriteExtensions))
            Member("Writes the extensions of the case this union holds.",
                $"public void WriteExtensions({Extensions} extensions) => PragmaticActiveCase.WriteExtensions(extensions);");
        if (Has(missing, UnionErrorMembers.WriteResolvedExtensions))
            Member("Writes the extensions of the case this union holds, in the caller's language.",
                $"public void WriteExtensions({Extensions} extensions, {Resolver} resolver) => " +
                "PragmaticActiveCase.WriteExtensions(extensions, resolver);");
    }

    private void Member(string summary, string declaration)
    {
        XmlSummary(summary);
        AppendLine(declaration);
        AppendLine();
    }

    private static bool Has(UnionErrorMembers set, UnionErrorMembers member) => (set & member) != 0;
}
