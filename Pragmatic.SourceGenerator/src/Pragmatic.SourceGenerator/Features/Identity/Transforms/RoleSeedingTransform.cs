using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Identity.Diagnostics;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Transforms;

/// <summary>
///     Parses roles.pragmatic.json content into a seeding model, reporting what it cannot read.
/// </summary>
/// <remarks>
///     ⚠️ Every failure is a diagnostic naming the file: not JSON is <c>PRAG1010</c> and seeds nothing;
///     anything else is <c>PRAG1011</c>, and what could be read is still seeded. Catching
///     <c>JsonException</c> and returning <c>null</c> would seed nothing and say nothing, and the roles the file
///     declares would not exist at runtime; a value of the wrong kind left to throw would take the whole
///     generator's output with it; a misspelt property ignored would leave the role granting nothing.
/// </remarks>
internal static class RoleSeedingTransform
{
    private static readonly string[] RoleProperties = ["description", "permissions", "inherits"];
    private static readonly string[] GroupProperties = ["description", "roles"];

    public static RoleSeedingAggregateModel? Parse(string json, string ns, string path, Action<Diagnostic> report)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException error)
        {
            report(Diagnostic.Create(IdentityDiagnostics.RoleSeedingFileNotJson,
                RoleSeedingReader.At(path, (int)(error.LineNumber ?? 0), (int)(error.BytePositionInLine ?? 0)), path, error.Message));
            return null;
        }

        using (document)
        {
            var reader = new RoleSeedingReader(path, report);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                reader.Problem("the file must be a JSON object with 'roles' and 'groups'");
                return null;
            }

            var roles = ImmutableArray<RoleSeedModel>.Empty;
            var groups = ImmutableArray<GroupSeedModel>.Empty;
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "roles":
                        roles = ParseRoles(property.Value, reader);
                        break;
                    case "groups":
                        groups = ParseGroups(property.Value, reader);
                        break;
                    default:
                        // A "$schema" is for the editor, not for the roles.
                        if (!property.Name.StartsWith("$", StringComparison.Ordinal))
                            reader.Problem($"'{property.Name}' is not a property of the file — it has 'roles' and 'groups'");
                        break;
                }
            }

            CheckInheritance(roles, reader);

            if (roles.IsEmpty && groups.IsEmpty)
                return null;

            return new RoleSeedingAggregateModel { Roles = roles, Groups = groups, Namespace = ns };
        }
    }

    private static ImmutableArray<RoleSeedModel> ParseRoles(JsonElement element, RoleSeedingReader reader)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            reader.Problem("'roles' must be an object, one property per role");
            return ImmutableArray<RoleSeedModel>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<RoleSeedModel>();
        foreach (var role in element.EnumerateObject())
        {
            if (role.Value.ValueKind != JsonValueKind.Object)
            {
                reader.Problem($"the role '{role.Name}' must be an object with 'permissions' — it is not seeded");
                continue;
            }

            reader.UnknownProperties(role.Value, RoleProperties, $"a role (the role '{role.Name}')");
            builder.Add(new RoleSeedModel
            {
                Name = role.Name,
                Description = reader.OptionalString(role.Value, "description", $"the role '{role.Name}'"),
                Permissions = reader.Strings(role.Value, "permissions", $"the role '{role.Name}'"),
                Inherits = reader.Strings(role.Value, "inherits", $"the role '{role.Name}'")
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<GroupSeedModel> ParseGroups(JsonElement element, RoleSeedingReader reader)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            reader.Problem("'groups' must be an object, one property per group");
            return ImmutableArray<GroupSeedModel>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<GroupSeedModel>();
        foreach (var group in element.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Object)
            {
                reader.Problem($"the group '{group.Name}' must be an object with 'roles' — it is not seeded");
                continue;
            }

            reader.UnknownProperties(group.Value, GroupProperties, $"a group (the group '{group.Name}')");
            builder.Add(new GroupSeedModel
            {
                Name = group.Name,
                Description = reader.OptionalString(group.Value, "description", $"the group '{group.Name}'"),
                Roles = reader.Strings(group.Value, "roles", $"the group '{group.Name}'")
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>An inherited role must be one of the file's, and roles cannot inherit each other.</summary>
    private static void CheckInheritance(ImmutableArray<RoleSeedModel> roles, RoleSeedingReader reader)
    {
        var byName = new Dictionary<string, RoleSeedModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
            byName[role.Name] = role;

        foreach (var role in roles)
        foreach (var parent in role.Inherits)
        {
            if (!byName.ContainsKey(parent))
                reader.Problem($"the role '{role.Name}' inherits '{parent}', which the file does not declare — it inherits nothing from it");
        }

        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            if (LoopFrom(role.Name, byName, [role.Name]) is { } loop && reported.Add(string.Join("|", loop.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))))
                reader.Problem($"the roles {string.Join(" → ", loop.Select(n => $"'{n}'"))} → '{loop[0]}' inherit each other");
        }
    }

    private static List<string>? LoopFrom(string start, Dictionary<string, RoleSeedModel> byName, List<string> chain)
    {
        if (!byName.TryGetValue(chain[chain.Count - 1], out var current))
            return null;

        foreach (var parent in current.Inherits)
        {
            if (string.Equals(parent, start, StringComparison.OrdinalIgnoreCase))
                return chain;
            if (chain.Contains(parent, StringComparer.OrdinalIgnoreCase))
                continue;

            if (LoopFrom(start, byName, [.. chain, parent]) is { } loop)
                return loop;
        }

        return null;
    }
}
