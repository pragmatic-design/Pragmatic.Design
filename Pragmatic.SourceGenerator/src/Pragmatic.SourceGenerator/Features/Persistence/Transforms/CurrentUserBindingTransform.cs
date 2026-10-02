using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;
using Pragmatic.SourceGenerator.Features.Identity.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads the <c>[FromCurrentUser]</c> properties of a query, then binds each one to what the invoker
///     can read: the caller's id, or a member of the <c>[PragmaticUser]</c> entity.
/// </summary>
internal static class CurrentUserBindingTransform
{
    /// <summary>What each <c>[FromCurrentUser]</c> property of the query declares.</summary>
    public static ImmutableArray<CurrentUserBindingModel> Extract(INamedTypeSymbol query, SemanticModel semanticModel)
    {
        var bindings = ImmutableArray.CreateBuilder<CurrentUserBindingModel>();

        foreach (var property in query.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || FromCurrentUserReader.Find(property) is not { } attribute)
                continue;

            var readable = FromCurrentUserReader.TryReadMember(attribute, semanticModel, out var member, out var qualifier);
            var setter = property.SetMethod;

            bindings.Add(new CurrentUserBindingModel
            {
                PropertyName = property.Name,
                PropertyTypeFullName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                PropertyIsString = property.Type.SpecialType == SpecialType.System_String,
                Member = member,
                MemberQualifier = qualifier,
                MemberIsUnreadable = !readable,
                IsAssignable = setter is { IsInitOnly: false },
                OnlyTheInvokerSetsIt = setter is { IsInitOnly: false, DeclaredAccessibility: Accessibility.Private },
                Location = LocationInfo.From(property.Locations.FirstOrDefault())
            });
        }

        return bindings.ToImmutable();
    }

    /// <summary>The query with each binding resolved against the application's user entity.</summary>
    /// <param name="model">The query as the transform read it.</param>
    /// <param name="users">Every <c>[PragmaticUser]</c> in the compilation, with whether its resolver is generated.</param>
    public static QueryModel Resolve(QueryModel model, EquatableArray<UserEntityModel> users)
    {
        if (model.CurrentUserBindings.Count == 0)
            return model;

        return model with { CurrentUserBindings = Resolve(model.CurrentUserBindings, users) };
    }

    /// <summary>The bindings of any operation, each resolved against the application's user entity.</summary>
    public static EquatableArray<CurrentUserBindingModel> Resolve(
        EquatableArray<CurrentUserBindingModel> bindings, EquatableArray<UserEntityModel> users)
        => bindings.Select(binding => Resolve(binding, users)).ToImmutableArray();

    private static CurrentUserBindingModel Resolve(CurrentUserBindingModel binding, EquatableArray<UserEntityModel> users)
    {
        if (binding.MemberIsUnreadable)
            return binding with
            {
                Problem = "the member's name cannot be read from the argument; write it as nameof(User.Member) or a string literal"
            };

        if (binding.Member is null)
            return binding.PropertyIsString
                ? binding
                : binding with
                {
                    Problem = $"without a member it binds ICurrentUser.Id, which is a string, and the property is '{Display(binding.PropertyTypeFullName)}'"
                };

        if (users.Count == 0)
            return binding with
            {
                Problem = $"there is no [PragmaticUser] entity in this compilation to read '{binding.Member}' from"
            };

        if (users.Count > 1)
            return binding with
            {
                Problem = $"there are {users.Count} [PragmaticUser] entities in this compilation, and the binding cannot tell which one is the user"
            };

        var user = users[0];
        var userTypeFullName = string.IsNullOrEmpty(user.Namespace)
            ? $"global::{user.TypeName}"
            : $"global::{user.Namespace}.{user.TypeName}";

        if (binding.MemberQualifier is { } qualifier && qualifier != userTypeFullName)
            return binding with
            {
                Problem = $"nameof names a member of '{Display(qualifier)}', and the user entity is '{user.TypeName}'"
            };

        if (!user.HasIdentityPersistence)
            return binding with
            {
                Problem = $"'{user.TypeName}' is read through its generated resolver, which exists only when Pragmatic.Identity.Persistence is referenced"
            };

        var member = user.Members.FirstOrDefault(m => m.Name == binding.Member);
        if (member is null)
            return binding with
            {
                Problem = $"'{user.TypeName}' has no public readable member '{binding.Member}'"
            };

        if (member.TypeFullName != binding.PropertyTypeFullName)
            return binding with
            {
                Problem = $"'{user.TypeName}.{member.Name}' is '{Display(member.TypeFullName)}' and the property is '{Display(binding.PropertyTypeFullName)}'"
            };

        return binding with
        {
            UserTypeFullName = userTypeFullName,
            UserTypeName = user.TypeName,
            ResolverTypeFullName = string.IsNullOrEmpty(user.Namespace)
                ? $"global::{UserResolverTemplate.ResolverNameFor(user)}"
                : $"global::{user.Namespace}.{UserResolverTemplate.ResolverNameFor(user)}"
        };
    }

    private static string Display(string fullyQualified)
        => fullyQualified.StartsWith("global::", System.StringComparison.Ordinal)
            ? fullyQualified.Substring("global::".Length)
            : fullyQualified;
}
