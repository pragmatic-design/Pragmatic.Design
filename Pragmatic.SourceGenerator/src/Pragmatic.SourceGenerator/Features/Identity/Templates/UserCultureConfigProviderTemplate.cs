using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

using static Pragmatic.SourceGenerator.Core.TemplateHelpers;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     Generates <c>{User}CultureConfigProvider</c> — the <c>II18NConfigProvider</c> at priority 200
///     that resolves the current user's <c>PreferredCulture</c>.
/// </summary>
/// <remarks>
///     <para>
///         The i18n chain documents 200–299 as "user preferences", and this class occupies that range:
///         it is what reads the <c>IUserProfile.PreferredCulture</c> the generator fills.
///     </para>
///     <para>
///         <b>Why <c>CachedConfigProvider</c>.</b> <c>GetConfiguration()</c> is synchronous, so every
///         request would otherwise be a database round-trip on the culture path — including requests
///         that never render a label. The base class keys a shared cache per provider type and the
///         derived type supplies the key, which here is the user: one read per user per five minutes.
///     </para>
///     <para>
///         <b>Only the UI culture.</b> The data culture is what serialization and storage format
///         against, and a per-user value there would make two users' API responses differ in shape. An
///         application that wants them together sets <c>SyncScopes</c>.
///     </para>
/// </remarks>
internal sealed class UserCultureConfigProviderTemplate : CSharpTemplate
{
    /// <summary>Where the chain reserves user preferences.</summary>
    private const int UserPriority = 200;

    /// <summary>How long a user's culture is held before it is read again.</summary>
    private const int CacheMinutes = 5;

    private readonly UserEntityModel _model;
    private readonly string _typeName;

    public UserCultureConfigProviderTemplate(UserEntityModel model)
    {
        _model = model;
        _typeName = NamingHelper.AppendSuffix(model.TypeName, "CultureConfigProvider");
    }

    /// <summary>The provider's fully qualified name, for whoever registers it.</summary>
    public static string FqnFor(UserEntityModel model)
    {
        var name = NamingHelper.AppendSuffix(model.TypeName, "CultureConfigProvider");
        return string.IsNullOrEmpty(model.Namespace) ? name : model.Namespace + "." + name;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Identity";
    protected override string? TriggerInfo => $"[ProfileProperty] PreferredCulture on {_model.TypeName}";

    /// <summary>
    ///     Nothing to resolve without the property, and nothing to resolve it with without the
    ///     resolver, which only exists when Identity.Persistence is referenced.
    /// </summary>
    protected override bool Validate()
        => _model.HasIdentityPersistence && DeclaresPreferredCulture(_model);

    /// <summary>True when the entity declares <c>PreferredCulture</c> as a profile property.</summary>
    public static bool DeclaresPreferredCulture(UserEntityModel model)
        => model.ProfileProperties.Any(p => p is { IsWellKnown: true, Name: "PreferredCulture" });

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_typeName, "CultureProvider", _model.Namespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Identity");
        AddUsing("Pragmatic.Internationalization.Context");
        AddUsing("Pragmatic.Internationalization.Types");

        if (!string.IsNullOrEmpty(_model.Namespace))
            AppendNamespace(_model.Namespace);

        AppendLine();

        XmlSummary(
            $"Resolves the UI culture from the current <see cref=\"{_model.TypeName}\"/>'s " +
            "PreferredCulture. Registered by UseUserCulture(); off until then.");

        Class(_typeName, RenderBody,
            baseType: "CachedConfigProvider",
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        var resolverName = UserResolverTemplate.ResolverNameFor(_model);

        AppendLine($"private readonly {resolverName} _users;");
        AppendLine("private readonly ICurrentUser _currentUser;");
        AppendLine();

        XmlSummary($"Creates a new {_typeName}.");
        XmlParam("users", "Loads the current user and projects their profile.");
        XmlParam("currentUser", "Identifies whose preference is being resolved, and keys the cache.");

        Constructor(_typeName, () =>
        {
            AppendLine("_users = users;");
            AppendLine("_currentUser = currentUser;");
        },
        [
            new MethodParameter(resolverName, "users"),
            new MethodParameter("ICurrentUser", "currentUser")
        ],
        AccessModifier.Public,
        baseCall: $"global::System.TimeSpan.FromMinutes({CacheMinutes})");

        XmlSummary(
            "User preferences, the band the chain reserves for them. Above tenant (100) and below the " +
            "request itself (300): an explicit ?culture= is still the user asking for something else.");
        AppendLine($"public override int Priority => {UserPriority};");
        AppendLine();

        XmlInheritDoc();
        AppendLine("protected override string? GetCacheKey()");
        IncreaseIndent();
        Comment("Null means \"do not cache\", so anonymous requests share nothing.");
        AppendLine("=> _currentUser.IsAuthenticated && !string.IsNullOrEmpty(_currentUser.Id)");
        AppendLine($"    ? \"i18n:user:\" + _currentUser.Id");
        AppendLine("    : null;");
        DecreaseIndent();
        AppendLine();

        XmlInheritDoc();
        AppendLine("protected override I18NConfig? LoadConfiguration()");
        Block(RenderLoad);
    }

    private void RenderLoad()
    {
        Comment("Checked here too, not only in GetCacheKey: a null key means the base class skips the " +
                "cache and calls this anyway, which would put a query on every anonymous request.");
        AppendLine("if (!_currentUser.IsAuthenticated) return null;");
        AppendLine();

        AppendLine("var profile = _users.GetProfile();");
        AppendLine();
        Comment("Null defers to the tenant and system providers rather than overriding them with " +
                "nothing — a user who has expressed no preference has not asked for the default to change.");
        AppendLine("if (string.IsNullOrEmpty(profile?.PreferredCulture)) return null;");
        AppendLine();

        Comment("A stored value that no longer parses defers as well. Failing the request over a stale " +
                "preference would lock the user out of the page where they could correct it.");
        AppendLine("return CultureCode.TryFromString(profile.PreferredCulture, out var culture)");
        AppendLine("    ? new I18NConfig { DefaultUICulture = culture }");
        AppendLine("    : null;");
    }
}
