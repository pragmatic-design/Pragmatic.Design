using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Identity.Models;
using Pragmatic.SourceGenerator.Features.Identity.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Reads the <c>[FromClock]</c> and <c>[FromCurrentUser]</c> properties of an action or a mutation
///     with the query's transforms, and its <c>[LoadCurrentUser]</c>, then binds the user side once the
///     user entity has arrived.
/// </summary>
internal static class InvokerBindingsTransform
{
    private const string LoadCurrentUserAttribute = "Pragmatic.Actions.Attributes.LoadCurrentUserAttribute";

    /// <summary>What each bound property of the operation declares.</summary>
    public static InvokerBindingsModel Extract(INamedTypeSymbol operation, SemanticModel semanticModel)
    {
        var clock = ClockBindingTransform.Extract(operation);
        var currentUser = CurrentUserBindingTransform.Extract(operation, semanticModel);
        var load = ExtractCurrentUserLoad(operation);

        return clock.IsEmpty && currentUser.IsEmpty && load is null
            ? InvokerBindingsModel.None
            : new InvokerBindingsModel { Clock = clock, CurrentUser = currentUser, CurrentUserLoad = load };
    }

    private static CurrentUserLoadModel? ExtractCurrentUserLoad(INamedTypeSymbol operation)
    {
        var attribute = operation.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == LoadCurrentUserAttribute);
        if (attribute is null)
            return null;

        var fieldName = attribute.NamedArguments
            .Where(a => a.Key == "FieldName")
            .Select(a => a.Value.Value as string)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

        return new CurrentUserLoadModel
        {
            FieldNameOverride = fieldName,
            Location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation())
        };
    }

    /// <summary>The bindings, with the user side resolved against the application's user entity.</summary>
    /// <remarks>
    ///     A stage of its own for the reason the query has one: the entity is another declaration of the
    ///     compilation, and it reaches this feature through the pipeline rather than by a search.
    /// </remarks>
    public static InvokerBindingsModel Resolve(InvokerBindingsModel bindings, EquatableArray<UserEntityModel> users)
    {
        if (bindings.CurrentUser.Count == 0 && bindings.CurrentUserLoad is null)
            return bindings;

        return bindings with
        {
            CurrentUser = bindings.CurrentUser.Count == 0
                ? bindings.CurrentUser
                : CurrentUserBindingTransform.Resolve(bindings.CurrentUser, users),
            CurrentUserLoad = bindings.CurrentUserLoad is { } load ? Resolve(load, users) : null
        };
    }

    private static CurrentUserLoadModel Resolve(CurrentUserLoadModel load, EquatableArray<UserEntityModel> users)
    {
        if (users.Count == 0)
            return load with { Problem = "there is no [PragmaticUser] entity in this compilation to load" };

        if (users.Count > 1)
            return load with
            {
                Problem = $"there are {users.Count} [PragmaticUser] entities in this compilation, and the load cannot tell which one is the user"
            };

        var user = users[0];
        if (!user.HasIdentityPersistence)
            return load with
            {
                Problem = $"'{user.TypeName}' is read through its generated resolver, which exists only when Pragmatic.Identity.Persistence is referenced"
            };

        var prefix = string.IsNullOrEmpty(user.Namespace) ? "global::" : $"global::{user.Namespace}.";
        return load with
        {
            UserTypeFullName = prefix + user.TypeName,
            UserTypeName = user.TypeName,
            ResolverTypeFullName = prefix + UserResolverTemplate.ResolverNameFor(user)
        };
    }
}
