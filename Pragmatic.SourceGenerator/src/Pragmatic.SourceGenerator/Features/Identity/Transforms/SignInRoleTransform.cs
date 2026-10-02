using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Transforms;

/// <summary>
///     The enum a <c>[SignsInAs&lt;TRole&gt;]</c> member belongs to, read whole: every member, with its role or without.
/// </summary>
/// <remarks>
///     Reached once per attributed member, and each reading is of the whole enum — the feature keeps one per enum.
///     Reading the enum rather than the member is what makes the mapping exhaustive: a member without the attribute
///     is found here, where a per-member pipeline would never see it.
/// </remarks>
internal static class SignInRoleTransform
{
    public const string Attribute = "Pragmatic.Authorization.SignsInAsAttribute`1";

    public static SignInRoleMapModel Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var member = (IFieldSymbol)context.TargetSymbol;
        var enumType = member.ContainingType;
        var arms = ImmutableArray.CreateBuilder<SignInRoleArmModel>();
        var unmapped = ImmutableArray.CreateBuilder<SignInRoleArmModel>();

        foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue))
        {
            var role = field.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.OriginalDefinition.MetadataName == "SignsInAsAttribute`1"
                                     && a.AttributeClass.ContainingNamespace.ToDisplayString() == "Pragmatic.Authorization")
                ?.AttributeClass!.TypeArguments[0];

            var arm = new SignInRoleArmModel
            {
                Member = field.Name,
                RoleFullName = role?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Location = LocationInfo.From(field.Locations.FirstOrDefault())
            };

            if (arm.RoleFullName is null)
                unmapped.Add(arm);
            else
                arms.Add(arm);
        }

        return new SignInRoleMapModel
        {
            EnumName = enumType.Name,
            Namespace = enumType.ContainingNamespace.IsGlobalNamespace ? "" : enumType.ContainingNamespace.ToDisplayString(),
            EnumFullName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsPublic = enumType.DeclaredAccessibility == Accessibility.Public,
            Arms = arms.ToImmutable(),
            Unmapped = unmapped.ToImmutable()
        };
    }
}
