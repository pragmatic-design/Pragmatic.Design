using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     Generates a user resolver service that loads the user entity using <c>ICurrentUser</c> claims, through the user's
///     <c>IUserLookup&lt;TUser&gt;</c>. Generated wherever such a lookup exists.
/// </summary>
/// <remarks>
///     <para>
///         The lookup is the persistence branch's: over EF Core's <c>Query()</c>, or the engines' generated reads. The resolver names none of
///         them, so it compiles without EF Core (#592).
///     </para>
///     <para>
///         Asynchronous only. The synchronous pair existed for <c>II18NConfigProvider.GetConfiguration()</c>; the culture provider now loads
///         through <c>LoadConfigurationAsync</c> (#591), and a synchronous read is what the engines do not have.
///     </para>
/// </remarks>
internal sealed class UserResolverTemplate : CSharpTemplate
{
    private readonly UserEntityModel _model;

    public UserResolverTemplate(UserEntityModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Identity";

    /// <summary>The resolver type name, shared with whoever injects it.</summary>
    public static string ResolverNameFor(UserEntityModel model)
        => NamingHelper.AppendSuffix(model.TypeName, "Resolver");

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "Resolver", _model.Namespace),
        ToSourceText());

    protected override bool Validate() =>
        !string.IsNullOrEmpty(_model.TypeName) && _model.HasUserLookup;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Identity");

        if (!string.IsNullOrEmpty(_model.Namespace))
            AppendNamespace(_model.Namespace);

        AppendLine();

        var resolverName = ResolverNameFor(_model);

        XmlSummary($"Resolves the current <see cref=\"{_model.TypeName}\"/> using <see cref=\"ICurrentUser\"/> claims.");
        AppendLine($"{_model.Accessibility} sealed class {resolverName}(");
        IncreaseIndent();
        AppendLine($"IUserLookup<{_model.TypeName}> users,");
        AppendLine("ICurrentUser currentUser)");
        DecreaseIndent();
        AppendLine("{");
        IncreaseIndent();

        RenderResolveMethod();
        AppendLine();
        RenderFindByIdentityKeyMethod();
        AppendLine();
        RenderGetProfileMethod();

        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderResolveMethod()
    {
        XmlSummary("Resolves the user entity for the current authenticated user.");
        AppendLine($"public async Task<{_model.TypeName}?> ResolveAsync(CancellationToken ct = default)");
        AppendLine("{");
        IncreaseIndent();

        var value = RenderMatchPreamble("null");
        AppendLine($"return await users.FindByIdentityKeyAsync({value}, ct).ConfigureAwait(false);");

        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     The lookup by a key the caller holds rather than the current user's — what a sign-in does
    ///     before anyone is authenticated, or a claims contributor for the account being signed in.
    /// </summary>
    /// <remarks>
    ///     On <see cref="UserEntityModel.MatchProperty" />, the property the current-user lookup matches:
    ///     an application writing this query itself is the second copy of "which property identifies
    ///     the user" that <see cref="RenderMatchPreamble" /> exists to avoid.
    /// </remarks>
    private void RenderFindByIdentityKeyMethod()
    {
        XmlSummary(
            $"Finds the user whose <c>{_model.MatchProperty}</c> is <paramref name=\"identityKey\"/>, " +
            "whoever is calling — the lookup a sign-in does before anyone is authenticated.");
        AppendLine($"public Task<{_model.TypeName}?> FindByIdentityKeyAsync(string identityKey, CancellationToken ct = default)");
        AppendLine("    => users.FindByIdentityKeyAsync(identityKey, ct);");
    }

    /// <summary>
    ///     Emits the guards that turn <c>ICurrentUser</c> into the value the lookup matches on, and
    ///     returns the expression holding it.
    /// </summary>
    /// <remarks>
    ///     One place for "which claim identifies the user". Two copies is the shape where one of them keeps working and the other silently
    ///     matches nobody.
    /// </remarks>
    private string RenderMatchPreamble(string noMatchResult)
    {
        AppendLine($"if (!currentUser.IsAuthenticated) return {noMatchResult};");
        AppendLine();

        var isExternalKey = _model.MatchProperty.EndsWith("ExternalIdentityKey");

        if (_model.MatchClaim == "sub" && isExternalKey)
        {
            // Ask the context for the key rather than composing it. IAuthenticationContext
            // .ExternalIdentityKey is the one definition of that format, and it percent-escapes both
            // halves so an issuer containing the separator cannot produce the same key as a
            // different issuer/subject pair. Reading the claim and building "{issuer}|{claim}" here
            // would be a second producer of the same value, missing exactly that escaping.
            AppendLine("var key = currentUser.Authentication.ExternalIdentityKey;");
            AppendLine($"if (string.IsNullOrEmpty(key)) return {noMatchResult};");
            AppendLine();

            return "key";
        }

        AppendLine($"var claimValues = currentUser.Claims.GetValueOrDefault(\"{_model.MatchClaim}\");");
        AppendLine($"if (claimValues is null || claimValues.Count == 0) return {noMatchResult};");
        AppendLine();

        return "claimValues[0]";
    }

    private void RenderGetProfileMethod()
    {
        XmlSummary("Resolves the user and returns their profile, or null if not found.");
        AppendLine("public async Task<IUserProfile?> GetProfileAsync(CancellationToken ct = default)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("var user = await ResolveAsync(ct).ConfigureAwait(false);");
        AppendLine("return user?.ToProfile();");
        DecreaseIndent();
        AppendLine("}");
    }
}
