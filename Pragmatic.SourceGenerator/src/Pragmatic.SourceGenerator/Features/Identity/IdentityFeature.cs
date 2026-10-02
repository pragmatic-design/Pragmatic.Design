using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Diagnostics;
using Pragmatic.SourceGenerator.Features.Identity.Models;
using Pragmatic.SourceGenerator.Features.Identity.Templates;
using Pragmatic.SourceGenerator.Features.Identity.Transforms;

namespace Pragmatic.SourceGenerator.Features.Identity;

/// <summary>
///     Identity/Authorization feature: generates the permission and role registries,
///     user profile adapters, and user resolvers.
///     Activated when Pragmatic.Authorization is referenced (HasAuthorization flag).
/// </summary>
internal static class IdentityFeature
{
    private const string SchemaVersion = MetadataSchemaVersions.Authorization;

    /// <summary>
    ///     Returns the catalog registration this compilation generates, for a host that declares its
    ///     permissions itself; and the <c>[PragmaticUser]</c> entities, for the query invokers that bind a
    ///     member of one with <c>[FromCurrentUser]</c>.
    /// </summary>
    public static (IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> LocalRegistrations,
        IncrementalValueProvider<EquatableArray<UserEntityModel>> Users) Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<EquatableArray<Core.PermissionConstEntry>> permissionCatalog,
        IncrementalValueProvider<EquatableArray<DeclaredPermissionModel>> declaredPermissions)
    {
        // The enum-to-role mapping of an access level: [SignsInAs<TRole>] on its members.
        SignsInAsFeature.Register(context);

        // The permission lists this assembly publishes for the compilations that cannot read them.
        // The catalogue comes along because a list is normally written from the constants this same run
        // generates, which nothing binds while the transform reads it.
        PermissionSetFeature.Register(context, permissionCatalog);

        // =====================================================================
        // Pipeline 1: IRole implementations. The permissions are declared on the assembly
        // (DeclaredPermissions), not as types.
        // =====================================================================

        var roleProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: IsTypeWithBaseList,
                transform: PermissionTransform.TransformRole)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.IdentityRoles);

        // =====================================================================
        // Aggregate: the declared permissions and the roles, with the feature flag
        // =====================================================================

        // [Role] classes: the generator writes their IRole members, so the syntax pipeline above — which
        // looks for a base list — does not see them, and they are not counted twice.
        var declaredRoles = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DeclaredRoleTransform.RoleAttribute,
                GeneratorHelpers.IsClass,
                DeclaredRoleTransform.Transform)
            .Collect();

        var allRoles = roleProvider.Collect().Combine(declaredRoles);

        // Assembly name disambiguates the generated registry/registration types. Two boundaries that
        // share a root namespace (e.g. Showcase.Billing + Showcase.Booking both derive prefix "Showcase")
        // would otherwise emit colliding `Showcase.PermissionRegistry`/`AuthorizationRegistrationExtensions`
        // types — the host would then bind the aggregation call to a single (shadowing) definition and
        // silently drop every other assembly's catalog entries.
        var assemblyName = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "");

        var combined = allRoles.Combine(features).Combine(assemblyName)
            .Combine(permissionCatalog).Combine(declaredPermissions);

        context.RegisterSourceOutputSafe(combined, static (ctx, data) =>
        {
            var (((((roles, roleClasses), featureFlags), assemblyName), catalog), declared) = data;
            if (!featureFlags.HasAuthorization)
                return;

            if (roles.IsDefaultOrEmpty && roleClasses.IsDefaultOrEmpty && declared.IsDefaultOrEmpty)
                return;

            // A role names constants this compilation cannot bind, because the same run generates
            // them. The catalogue is where those names have values, and this is the level that holds
            // it: the transform sees one compilation, so it declared the names instead of guessing.
            roles = WithCataloguedPermissions(roles, catalog);

            ValidateRoles(ctx, roles);

            // A role whose name could not be resolved has just been reported. Drop it rather than emit a
            // registry entry with an empty name — a role nobody can ever hold, looking like a real one.
            roles = WithUsableName(roles, static r => r.Name);

            // A spread the catalogue cannot follow: the runtime grants more than the registry will list.
            foreach (var role in roles)
            foreach (var spread in role.UnreadableSpreads)
                ctx.ReportDiagnostic(Diagnostic.Create(
                    IdentityDiagnostics.RoleSpreadCannotBeRead, Location.None, role.TypeName, spread));

            // The whole list lives in an assembly that does not publish it: the registry would list an
            // empty grant, which is what a role that grants nothing says too.
            foreach (var role in roles)
            foreach (var reference in role.UnreadableReferences)
                ctx.ReportDiagnostic(Diagnostic.Create(
                    IdentityDiagnostics.RoleListCannotBeRead, Location.None, role.TypeName, reference));

            // The [Role] classes, flattened through the roles they include — the hand-written ones among
            // them — and the permission catalogue. The members written into each and its registry entry
            // carry the same list; a hand-written role spreading one of them takes that list too.
            var generated = DeclaredRoleResolver.WithGeneratedMembers(ctx, roleClasses, roles, catalog).ToImmutableArray();
            roles = WithSpreadRoles(roles, generated).AddRange(generated);

            var namespacePrefix = DeriveNamespacePrefix(roles);

            // The declared permissions are the registry's permissions: a role screen lists them. Their
            // constants are the permissions class's (PermissionsClassFeature), not written here — there is
            // one {Boundary}Permissions.
            var permissions = RegistryEntries(declared);

            if (permissions.IsDefaultOrEmpty && roles.IsDefaultOrEmpty)
                return;

            var catalogNamespace = DeriveCatalogNamespace(assemblyName, namespacePrefix);

            // Generate unified registry into the assembly-unique catalog namespace.
            GenerateRegistry(ctx, permissions, roles, catalogNamespace);

            // #D-C1: emit the DI registration that actually feeds PermissionRegistry.All/RoleRegistry.All
            // into DefaultPermissionCatalog, plus the host-discoverable metadata that invokes it.
            GenerateAuthorizationCatalogRegistration(
                ctx, permissions, roles, catalogNamespace, featureFlags.HasComposition);
        });

        // Same condition as the metadata emitted inside GenerateAuthorizationCatalogRegistration: a
        // host that declares its own permissions or roles never sees that attribute.
        var localRegistrations = combined.Select(static (data, _) =>
        {
            var (((((roles, roleClasses), featureFlags), assemblyName), _), declared) = data;
            if (!featureFlags.HasAuthorization || !featureFlags.HasComposition)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;
            if (roles.IsDefaultOrEmpty && roleClasses.IsDefaultOrEmpty && declared.IsDefaultOrEmpty)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            // Only the fallback when the assembly is unnamed, so the [Role] classes' namespaces count here as
            // well: the registration's namespace must be the one the output step derives.
            var catalogNamespace = DeriveCatalogNamespace(assemblyName,
                NamespacePrefixHelper.ToIdentifier(NamespacePrefixHelper.DerivePrefix(
                    roles.Select(r => r.Namespace).Concat(roleClasses.Select(r => r.Namespace)))));

            return ImmutableArray.Create(
                Composition.Models.HostLocalRegistration.Create(
                    Composition.MetadataCategoryIds.Authorization,
                    SchemaVersion,
                    AuthorizationRegistrationTemplate.FqnFor(catalogNamespace)));
        });

        // =====================================================================
        // Pipeline 3: [PragmaticUser] — profile + resolver + culture provider
        // =====================================================================

        var userEntityProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.PragmaticUser,
                GeneratorHelpers.IsClass,
                UserEntityTransform.Transform)
            .Where(static m => m is not null);

        context.RegisterSourceOutputSafe(
            userEntityProvider.Combine(features).Combine(assemblyName),
            static (ctx, data) =>
            {
                var ((model, featureFlags), assemblyName) = data;
                if (model is null) return;

                // Enrich model with persistence flag
                var enriched = model with { HasIdentityPersistence = featureFlags.HasIdentityPersistence };

                // Always generate the profile adapter
                GenerateUserProfile(ctx, enriched);

                // Generate resolver only if Identity.Persistence is referenced
                if (enriched.HasIdentityPersistence)
                    GenerateUserResolver(ctx, enriched);

                GenerateUserCultureProvider(ctx, enriched, featureFlags, assemblyName);
            });

        // =====================================================================
        // Pipeline 4: roles.pragmatic.json seeding (AdditionalFile)
        // =====================================================================

        // The path travels with the content: every diagnostic about the file names it.
        var seedingFileProvider = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith("roles.pragmatic.json", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, ct) => (file.Path, Content: file.GetText(ct)?.ToString() ?? ""))
            .Where(static file => !string.IsNullOrEmpty(file.Content))
            .Collect();

        var seedingWithFeatures = seedingFileProvider
            .Combine(features)
            .Combine(context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? ""));

        context.RegisterSourceOutputSafe(seedingWithFeatures, static (ctx, data) =>
        {
            var ((files, featureFlags), assemblyName) = data;
            if (!featureFlags.HasAuthorization || files.IsDefaultOrEmpty)
                return;

            // One file is read; another one's roles would not exist, so it is reported rather than ignored.
            for (var i = 1; i < files.Length; i++)
                ctx.ReportDiagnostic(Diagnostic.Create(IdentityDiagnostics.RoleSeedingFileIgnored,
                    RoleSeedingReader.At(files[i].Path, 0, 0), files[i].Path, files[0].Path));

            var model = RoleSeedingTransform.Parse(files[0].Content, assemblyName, files[0].Path, ctx.ReportDiagnostic);
            if (model is null)
                return;

            var template = new RoleSeedingTemplate(model);
            var artifact = template.RenderOutput();
            if (!artifact.IsEmpty)
                ctx.AddSource(artifact);
        });

        // The same models the resolver is generated from, enriched by the same flag that decides whether
        // it is: a binding naming a resolver this feature did not write would not compile.
        var users = userEntityProvider
            .Select(static (m, _) => m!)
            .Collect()
            .Combine(features)
            .Select(static (pair, _) => new EquatableArray<UserEntityModel>(pair.Left
                .Select(m => m with { HasIdentityPersistence = pair.Right.HasIdentityPersistence })
                .ToImmutableArray()));

        return (localRegistrations, users);
    }

    /// <summary>
    ///     Any type declaration that could implement <c>IRole</c>. Not limited to <c>class</c>: a record or a
    ///     struct declaring a role would otherwise be skipped in silence, and nothing in the language or the
    ///     docs makes those illegal. Interfaces are excluded: one that derives
    ///     from <c>IRole</c> is a contract, not a definition.
    /// </summary>
    private static bool IsTypeWithBaseList(SyntaxNode node, CancellationToken _)
        => node is TypeDeclarationSyntax { BaseList.Types.Count: > 0 } and not InterfaceDeclarationSyntax;

    /// <summary>
    ///     The custom permissions this assembly declares: <c>[assembly: Permission(…)]</c>, and the
    ///     <c>[RequirePermission("…", Description = "…")]</c> that declare one in passing (Mode 2).
    /// </summary>
    /// <remarks>
    ///     One provider for every reader — the permissions class, the permission registry, the constant
    ///     catalogue — so the three cannot disagree about which permissions exist. Ordered by value, so a
    ///     "declared twice" names the same pair on every run.
    /// </remarks>
    public static IncrementalValueProvider<EquatableArray<DeclaredPermissionModel>> DeclaredPermissions(
        IncrementalGeneratorInitializationContext context)
    {
        var fromAssembly = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DeclaredPermissionTransform.AttributeName,
                static (node, _) => node is CompilationUnitSyntax,
                DeclaredPermissionTransform.FromAssembly)
            .WithTrackingName(TrackingNames.IdentityPermissions)
            .Collect();

        var fromRequirements = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Pragmatic.Authorization.RequirePermissionAttribute",
                GeneratorHelpers.IsClass,
                CustomPermissionTransform.Transform)
            .WithTrackingName(TrackingNames.IdentityCustomPermissions)
            .Collect();

        return fromAssembly.Combine(fromRequirements).Select(static (pair, _) =>
            new EquatableArray<DeclaredPermissionModel>(pair.Left.SelectMany(static a => a)
                .Concat(pair.Right.SelectMany(static r => r))
                .OrderBy(static p => p.Value, StringComparer.Ordinal)
                .ThenBy(static p => p.Source, StringComparer.Ordinal)
                .ToImmutableArray()));
    }

    /// <summary>Adds to each hand-written role the list of every <c>[Role]</c> class it spreads.</summary>
    private static ImmutableArray<RoleModel> WithSpreadRoles(
        ImmutableArray<RoleModel> roles, ImmutableArray<RoleModel> generated)
    {
        if (generated.IsDefaultOrEmpty || roles.All(static r => r.SpreadRoles.Length == 0))
            return roles;

        var byType = generated.ToDictionary(static r => r.SourceTypeFqn, StringComparer.Ordinal);
        return roles.Select(role =>
        {
            if (role.SpreadRoles.Length == 0)
                return role;

            var granted = role.DefaultPermissions.AsImmutableArray().ToBuilder();
            foreach (var spread in role.SpreadRoles)
            {
                if (!byType.TryGetValue(spread, out var source))
                    continue;
                foreach (var permission in source.DefaultPermissions)
                {
                    if (!granted.Contains(permission))
                        granted.Add(permission);
                }
            }

            return role with { DefaultPermissions = new EquatableArray<string>(granted.ToImmutable()) };
        }).ToImmutableArray();
    }

    /// <summary>One registry entry per declared value — the first declaration of a value wins, a second is PRAG1001.</summary>
    private static ImmutableArray<PermissionModel> RegistryEntries(EquatableArray<DeclaredPermissionModel> declared)
    {
        if (declared.IsDefaultOrEmpty)
            return ImmutableArray<PermissionModel>.Empty;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = ImmutableArray.CreateBuilder<PermissionModel>();
        foreach (var d in declared)
        {
            // An empty value is PRAG1004, from the permissions class: no entry nobody could be granted.
            if (string.IsNullOrWhiteSpace(d.Value) || !names.Add(d.Value))
                continue;

            builder.Add(new PermissionModel
            {
                TypeName = d.Value,
                Accessibility = "public",
                TypeKind = "attribute",
                Name = d.Value,
                Description = d.Description,
                Category = d.Category,
                SourceTypeFqn = d.Source
            });
        }

        return builder.ToImmutable();
    }

    // =========================================================================
    // Validation
    // =========================================================================

    /// <summary>
    ///     Keeps only the declarations whose name could be resolved. The unresolvable ones have already
    ///     been reported by the validation above.
    /// </summary>
    private static ImmutableArray<T> WithUsableName<T>(ImmutableArray<T> models, Func<T, string> nameOf)
        => models.IsDefaultOrEmpty
            ? models
            : models.Where(m => !string.IsNullOrEmpty(nameOf(m))).ToImmutableArray();

    private static void ValidateRoles(
        SourceProductionContext ctx, ImmutableArray<RoleModel> roles)
    {
        // Roles validation is minimal for L1
        foreach (var r in roles)
        {
            if (string.IsNullOrEmpty(r.Name))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    IdentityDiagnostics.EmptyRoleName,
                    Location.None, r.TypeName));
            }
        }
    }

    // =========================================================================
    // Generation
    // =========================================================================

    private static void GenerateRegistry(
        SourceProductionContext ctx,
        ImmutableArray<PermissionModel> permissions,
        ImmutableArray<RoleModel> roles,
        string namespacePrefix)
    {
        var template = new PermissionRegistryTemplate(permissions, roles, namespacePrefix);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            ctx.AddSource(artifact);
    }

    /// <summary>
    ///     #D-C1: emits the per-assembly <c>AddGeneratedAuthorizationCatalog</c> extension (registers each
    ///     generated <c>PermissionInfo</c>/<c>RoleInfo</c> as a singleton) plus the Authorization metadata
    ///     attribute the host uses to invoke it. Without this the generated registries were never consumed
    ///     and <c>IPermissionCatalog</c>'s static lists were always empty.
    /// </summary>
    private static void GenerateAuthorizationCatalogRegistration(
        SourceProductionContext ctx,
        ImmutableArray<PermissionModel> permissions,
        ImmutableArray<RoleModel> roles,
        string namespacePrefix,
        bool hasComposition)
    {
        var hasPermissions = permissions.Length > 0;
        var hasRoles = roles.Length > 0;
        if (!hasPermissions && !hasRoles)
            return;

        var registration = new AuthorizationRegistrationTemplate(namespacePrefix, hasPermissions, hasRoles);
        var regArtifact = registration.RenderOutput();
        if (!regArtifact.IsEmpty)
            ctx.AddSource(regArtifact);

        // The host discovers + invokes the registration through metadata on referenced assemblies.
        // Only meaningful when Composition (host aggregation) is in play.
        if (!hasComposition)
            return;

        var metadata = new AuthorizationMetadataTemplate(
            registration.RegistrationMethodFqn, permissions.Length, roles.Length, indent: false);
        var metaArtifact = metadata.RenderOutput();
        if (!metaArtifact.IsEmpty)
            ctx.AddSource(metaArtifact);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <remarks>
    ///     The fallback namespace of the registry, so it must be a function of the input set alone. Reading
    ///     <c>permissions[0].Namespace</c> would take whatever the incremental provider happened to
    ///     enumerate first, which could move the registry between two runs over identical source.
    ///     <see cref="NamespacePrefixHelper.DerivePrefix"/> sorts first.
    /// </remarks>
    private static string DeriveNamespacePrefix(ImmutableArray<RoleModel> roles)
        => NamespacePrefixHelper.ToIdentifier(
            NamespacePrefixHelper.DerivePrefix(roles.Select(r => r.Namespace)));

    /// <summary>
    ///     Derives the namespace for the generated PermissionRegistry/RoleRegistry and the
    ///     AuthorizationRegistrationExtensions. Uses the (sanitized) assembly name so the types are
    ///     unique per assembly — sharing a root namespace prefix across boundaries would collide.
    ///     Falls back to the namespace prefix (or Pragmatic.Authorization) when the assembly is unnamed.
    /// </summary>
    private static string DeriveCatalogNamespace(string assemblyName, string namespacePrefix)
    {
        var sanitized = SanitizeNamespace(assemblyName);
        if (!string.IsNullOrEmpty(sanitized))
            return sanitized;

        return !string.IsNullOrEmpty(namespacePrefix) ? namespacePrefix : "Pragmatic.Authorization";
    }

    private static string SanitizeNamespace(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '_')
                chars[i] = '_';
        }

        var result = new string(chars);

        // A namespace segment must not start with a digit; prefix such segments with '_'.
        var segments = result.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            if (seg.Length > 0 && char.IsDigit(seg[0]))
                segments[i] = "_" + seg;
        }

        return string.Join(".", segments);
    }

    // =========================================================================
    // [PragmaticUser] generation
    // =========================================================================

    private static void GenerateUserProfile(
        SourceProductionContext ctx, UserEntityModel model)
    {
        var template = new UserProfileTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            ctx.AddSource(artifact);
    }

    private static void GenerateUserResolver(
        SourceProductionContext ctx, UserEntityModel model)
    {
        var template = new UserResolverTemplate(model);
        var artifact = template.RenderOutput();
        if (!artifact.IsEmpty)
            ctx.AddSource(artifact);
    }

    /// <summary>
    ///     The resolvers this feature writes, described as services for Composition to register.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Described here because a <c>[Service]</c> scan cannot find a type this same generator
    ///         writes. Without a registration the resolver could only be built with <c>new</c> — which is
    ///         what the query invokers did — and an application that needed the current user's entity
    ///         wrote its own lookup, a second copy of the match the resolver owns.
    ///     </para>
    ///     <para>
    ///         The same condition the resolver is generated on, so a registration never names a type
    ///         that was not written; and public only, because the host registers a module's services by
    ///         naming them from its own assembly. Scoped: it reads the current user.
    ///     </para>
    /// </remarks>
    internal static EquatableArray<Composition.Models.ServiceModel> ResolverServices(
        EquatableArray<UserEntityModel> users)
    {
        var services = ImmutableArray.CreateBuilder<Composition.Models.ServiceModel>();

        foreach (var user in users)
        {
            if (!user.HasIdentityPersistence || string.IsNullOrEmpty(user.TypeName) || user.Accessibility != "public")
                continue;

            var prefix = string.IsNullOrEmpty(user.Namespace) ? "global::" : $"global::{user.Namespace}.";
            var resolver = $"{prefix}{UserResolverTemplate.ResolverNameFor(user)}";

            services.Add(new Composition.Models.ServiceModel
            {
                Namespace = user.Namespace,
                TypeName = UserResolverTemplate.ResolverNameFor(user),
                FullTypeName = resolver,
                ServiceTypeName = resolver,
                Lifetime = "Scoped",
                AsSelf = true,
                // Both are registered by the host, and this description has to say so: a dependency
                // built from names has no symbol to read [ProvidedByHost] from.
                Dependencies = ImmutableArray.Create(
                    new Composition.Models.DependencyModel
                    {
                        TypeName = "IReadRepository",
                        FullTypeName = $"global::Pragmatic.Persistence.Repository.IReadRepository<{prefix}{user.TypeName}>",
                        IsOptional = false,
                        IsProvidedByHost = true,
                        ProvidedByHostLifetime = "Scoped"
                    },
                    new Composition.Models.DependencyModel
                    {
                        TypeName = "ICurrentUser",
                        FullTypeName = "global::Pragmatic.Identity.ICurrentUser",
                        IsOptional = false,
                        IsProvidedByHost = true,
                        ProvidedByHostLifetime = "Scoped"
                    })
            });
        }

        return services.ToImmutable();
    }

    /// <summary>
    ///     Emits the per-user culture provider and the <c>UseUserCulture()</c> that turns it on.
    /// </summary>
    /// <remarks>
    ///     Both or neither: a provider nothing can register is the shape this work exists to remove,
    ///     and a registration naming a type that was not emitted does not compile.
    /// </remarks>
    private static void GenerateUserCultureProvider(
        SourceProductionContext ctx, UserEntityModel model, DetectedFeatures features, string assemblyName)
    {
        // Needs the i18n contracts to implement, the resolver to read through, and the Composition
        // builder the opt-in hangs off.
        if (!features.HasI18n || !features.HasIdentityPersistence || !features.HasComposition)
            return;

        // The same condition the provider template validates on. Asked here as well because a template
        // whose Validate() fails still returns an artifact — an empty one — so branching on its output
        // would emit a registration naming a class that was never written.
        if (!UserCultureConfigProviderTemplate.DeclaresPreferredCulture(model))
            return;

        SourceOutput.AddSource(ctx, new UserCultureConfigProviderTemplate(model).RenderOutput());

        SourceOutput.AddSource(ctx, new UserCultureRegistrationTemplate(
            UserCultureConfigProviderTemplate.FqnFor(model),
            $"{model.Namespace}.{UserResolverTemplate.ResolverNameFor(model)}",
            GeneratedRegistrationNames.InGeneratedNamespace(assemblyName)).RenderOutput());
    }

    /// <summary>
    ///     Adds, to each role's grant, the values of the constant paths the compilation could not
    ///     bind.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Dropping those paths where they are read would catalogue a role granting four entity
    ///     permissions as granting only the hand-written one, while the runtime — which reads the
    ///     property — grants all four. Nothing would fail: what is applied stays right, and only the
    ///     catalogue lies, so the screen listing a role's grants would state a falsehood nobody
    ///     compares.
    /// </remarks>
    private static ImmutableArray<RoleModel> WithCataloguedPermissions(
        ImmutableArray<RoleModel> roles,
        EquatableArray<Core.PermissionConstEntry> catalog)
    {
        if (roles.IsDefaultOrEmpty)
            return roles;

        var byPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in catalog)
            byPath[entry.ConstPath] = entry.Value;

        var builder = ImmutableArray.CreateBuilder<RoleModel>(roles.Length);

        foreach (var role in roles)
        {
            if (role.UnresolvedDefaultPermissions.Length == 0)
            {
                builder.Add(role);
                continue;
            }

            var granted = ImmutableArray.CreateBuilder<string>();
            granted.AddRange(role.DefaultPermissions.AsImmutableArray());

            foreach (var path in role.UnresolvedDefaultPermissions)
            {
                // A path with no entry stays unresolved rather than being invented: the alternative
                // is a catalogue that names a permission nobody can be granted.
                if (byPath.TryGetValue(path, out var value) && !granted.Contains(value))
                    granted.Add(value);
            }

            builder.Add(role with { DefaultPermissions = new EquatableArray<string>(granted.ToImmutable()) });
        }

        return builder.ToImmutable();
    }
}
