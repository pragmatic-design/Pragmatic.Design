using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Identity.Diagnostics;
using Pragmatic.SourceGenerator.Features.Identity.Models;
using Pragmatic.SourceGenerator.Features.Identity.Templates;
using Pragmatic.SourceGenerator.Features.Identity.Transforms;

namespace Pragmatic.SourceGenerator.Features.Identity;

/// <summary>
///     <c>[SignsInAs&lt;TRole&gt;]</c> on enum members: the enum-to-role mapping, generated and exhaustive.
/// </summary>
/// <remarks>
///     Its own feature rather than a stage of <see cref="IdentityFeature" />: it reads nothing of the permission
///     catalogue or the role registry — the arms name each role's <c>Name</c>, resolved by the compiler.
/// </remarks>
internal static class SignsInAsFeature
{
    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        var maps = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SignInRoleTransform.Attribute,
                static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.EnumMemberDeclarationSyntax,
                SignInRoleTransform.Transform)
            .Collect();

        context.RegisterSourceOutputSafe(maps, static (ctx, all) =>
        {
            // One reading per attributed member, each of the whole enum: keep one per enum.
            foreach (var map in all.GroupBy(m => m.EnumFullName).Select(g => g.First()))
            {
                foreach (var member in map.Unmapped)
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        IdentityDiagnostics.SignInMemberWithoutRole, member.Location?.ToLocation() ?? Location.None,
                        map.EnumName, member.Member));

                var artifact = new SignInRoleNamesTemplate(map).RenderOutput();
                if (!artifact.IsEmpty)
                    ctx.AddSource(artifact);
            }
        });
    }
}
