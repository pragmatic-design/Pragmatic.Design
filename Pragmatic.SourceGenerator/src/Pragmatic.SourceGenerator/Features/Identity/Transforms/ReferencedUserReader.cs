using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Identity.Models;
using Pragmatic.SourceGenerator.Features.Identity.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Identity.Transforms;

/// <summary>
///     The <c>[PragmaticUser]</c> entities of the modules a compilation references, for the <c>[FromCurrentUser]</c> and
///     <c>[LoadCurrentUser]</c> of a module that does not declare the user itself (#636).
/// </summary>
/// <remarks>
///     <para>
///         "My tasks" lives with the tasks and the user with the accounts. The user's module has already been compiled when the
///         referencing one is, generated members included: its resolver is a symbol here, not something another run will write, so
///         whether the invoker can build it is read, not assumed.
///     </para>
///     <para>
///         Every type of every non-framework reference is visited, which is why the feature asks only when a binding needs a user and
///         the compilation has none of its own. Only modules that reference the attribute's assembly can declare one.
///     </para>
/// </remarks>
internal static class ReferencedUserReader
{
    public static EquatableArray<UserEntityModel> Read(Compilation compilation, CancellationToken ct)
    {
        if (compilation.GetTypeByMetadataName(AttributeNames.PragmaticUser) is not { } attributeType)
            return EquatableArray<UserEntityModel>.Empty;

        var declaringName = attributeType.ContainingAssembly.Name;
        var users = ImmutableArray.CreateBuilder<UserEntityModel>();

        foreach (var reference in compilation.References)
        {
            ct.ThrowIfCancellationRequested();

            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly
                || EntityMetadataReader.IsFrameworkAssembly(assembly.Name)
                || !References(assembly, declaringName))
                continue;

            foreach (var type in EntityMetadataReader.GetAllNamedTypes(assembly.GlobalNamespace, ct))
            {
                var attribute = type.GetAttributes()
                    .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType));
                if (attribute is null || UserEntityTransform.FromSymbol(type, attribute) is not { } user)
                    continue;

                users.Add(user with
                {
                    DeclaringAssembly = assembly.Name,
                    HasReferencedResolver = HasPublicResolver(assembly, user)
                });
            }
        }

        return users.ToImmutable();
    }

    private static bool References(IAssemblySymbol assembly, string name)
        => assembly.Modules.Any(m => m.ReferencedAssemblies.Any(r => r.Name == name));

    /// <summary>
    ///     Whether the user's module carries its resolver where the referencing module can build it: the user's module writes it with
    ///     the user's accessibility, where the user has a lookup.
    /// </summary>
    private static bool HasPublicResolver(IAssemblySymbol assembly, UserEntityModel user)
    {
        var name = UserResolverTemplate.ResolverNameFor(user);
        var resolver = assembly.GetTypeByMetadataName(string.IsNullOrEmpty(user.Namespace) ? name : $"{user.Namespace}.{name}");
        return user.Accessibility == "public" && resolver is { DeclaredAccessibility: Accessibility.Public };
    }
}
