using System.Collections.Generic;
using System.Text;
using Pragmatic.SourceGenerator.Features.Resource.Models;

namespace Pragmatic.SourceGenerator.Features.Resource.Templates;

/// <summary>
///     Writes, into the file the generator produces for an operation, what was decided about that
///     operation somewhere else.
/// </summary>
/// <remarks>
///     <para>
///         This is the price of putting the decisions on the operation instead of on the entity, paid
///         rather than avoided. <c>[Resource("guests", Capabilities = All)]</c> now says which operations
///         exist and nothing about what any of them requires or answers with; and the file where a
///         developer says so — <c>public partial class ResourceReadGuestQuery;</c> with two attributes
///         and no body — is not somewhere you would think to look.
///     </para>
///     <para>
///         So the generated declaration says it: the permission in force, the shape it answers with, and
///         whether either came from a file of yours. It is what IntelliSense shows on the type, and the
///         one place that necessarily knows both halves.
///     </para>
/// </remarks>
internal static class ResourceDecorationRemark
{
    /// <summary>
    ///     The <c>&lt;remarks&gt;</c> lines for one operation, or nothing when there is nothing to say.
    /// </summary>
    /// <param name="decoration">What the developer declared on this operation, if anything.</param>
    /// <param name="typeName">The operation's type name, for the sentence that explains how to change it.</param>
    /// <param name="scaffoldedPermissions">The permission the scaffolding requires by default.</param>
    /// <param name="answersWith">The type the operation answers with, or null when it answers with nothing.</param>
    public static IEnumerable<string> Lines(
        ResourceOverrideModel? decoration,
        string typeName,
        IReadOnlyList<string> scaffoldedPermissions,
        string? answersWith = null)
    {
        var permission = PermissionSentence(decoration, scaffoldedPermissions);
        var shape = ShapeSentence(decoration, answersWith);

        if (permission is null && shape is null)
            yield break;

        yield return "/// <remarks>";

        if (permission is not null)
            yield return $"/// {permission}";

        if (shape is not null)
            yield return $"/// {shape}";

        // Only worth saying when nothing has been declared yet. Once a part exists, the developer knows
        // where it is.
        if (decoration is null)
        {
            var subject = permission is not null && shape is not null ? "either" : "this";
            yield return $"/// To change {subject}, declare <c>partial class {typeName}</c> in a file of";
            yield return "/// your own and put the attributes on it — yours replace these rather than adding";
            yield return "/// to them.";
        }

        yield return "/// </remarks>";
    }

    private static string? PermissionSentence(
        ResourceOverrideModel? decoration, IReadOnlyList<string> scaffolded)
    {
        if (decoration is null)
        {
            return scaffolded.Count == 0
                ? null
                : $"Requires <c>{Join(scaffolded)}</c>.";
        }

        if (decoration.AllowAnonymous)
            return "Open to anyone: your own part declares <c>[AllowAnonymous]</c>.";

        if (decoration.PolicyTypeFullName is not null)
            return $"Governed by <c>{decoration.PolicyTypeFullName}</c>, declared on your own part.";

        var required = Declared(decoration);
        if (required.Count > 0)
            return $"Requires <c>{Join(required)}</c>, declared on your own part.";

        // The part exists but says nothing about authorization — so the scaffolded default still holds.
        return scaffolded.Count == 0 ? null : $"Requires <c>{Join(scaffolded)}</c>.";
    }

    private static string? ShapeSentence(ResourceOverrideModel? decoration, string? answersWith)
    {
        if (answersWith is null)
            return null;

        return decoration?.DeclaredDto is { } declared
            ? $"Answers with <c>{declared.DtoFullTypeName}</c>, declared on your own part."
            : $"Answers with <c>{answersWith}</c>.";
    }

    /// <summary>The permissions the developer's part declares, resolved ones and const paths alike.</summary>
    /// <remarks>
    ///     A path is shown as written rather than resolved: this runs while rendering, and a constant
    ///     this generator emits has no value yet at that point. Showing the path is what the developer
    ///     wrote, so it is at least true.
    /// </remarks>
    private static List<string> Declared(ResourceOverrideModel decoration)
    {
        var all = new List<string>();
        all.AddRange(decoration.RequireAllPermissions);
        all.AddRange(decoration.UnresolvedRequireAllPaths);
        all.AddRange(decoration.RequireAnyPermissions);
        all.AddRange(decoration.UnresolvedRequireAnyPaths);
        return all;
    }

    private static string Join(IReadOnlyList<string> values)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0) builder.Append(", ");
            builder.Append(values[i]);
        }

        return builder.ToString();
    }
}
