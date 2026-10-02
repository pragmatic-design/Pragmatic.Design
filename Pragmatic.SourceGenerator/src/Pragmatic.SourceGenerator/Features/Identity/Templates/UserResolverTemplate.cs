using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     Generates a user resolver service that loads the user entity from the database
///     using <c>ICurrentUser</c> claims. Only generated when Identity.Persistence is referenced.
/// </summary>
/// <remarks>
///     Both an asynchronous and a synchronous pair are emitted. The synchronous one is not a
///     convenience: <c>II18NConfigProvider.GetConfiguration()</c> is synchronous, so a caller on that
///     path has only two ways to reach the database — a real synchronous query, or blocking on an
///     asynchronous one. The second is the sync-over-async this codebase refuses; the first is what EF
///     Core has always supported, and it is behind a five-minute cache.
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
        !string.IsNullOrEmpty(_model.TypeName) && _model.HasIdentityPersistence;

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Pragmatic.Identity");
        AddUsing("Pragmatic.Persistence.Repository");

        if (!string.IsNullOrEmpty(_model.Namespace))
            AppendNamespace(_model.Namespace);

        AppendLine();

        var resolverName = ResolverNameFor(_model);

        XmlSummary($"Resolves the current <see cref=\"{_model.TypeName}\"/> from the database using <see cref=\"ICurrentUser\"/> claims.");
        AppendLine($"{_model.Accessibility} sealed class {resolverName}(");
        IncreaseIndent();
        // The repository, not a DbContext. A generated resolver lives in the module assembly, and
        // the boundary's context is generated in the HOST — it cannot be named from here. The base
        // Microsoft.EntityFrameworkCore.DbContext can, but a Pragmatic application never registers
        // it: there is one context per boundary, so asking for it would stop the host booting at
        // ValidateOnBuild. IReadRepository<TEntity> is registered by the framework
        // and reaches across the assembly boundary, which is what this needs.
        AppendLine($"IReadRepository<{_model.TypeName}> repository,");
        AppendLine("ICurrentUser currentUser)");
        DecreaseIndent();
        AppendLine("{");
        IncreaseIndent();

        RenderResolveMethod();
        AppendLine();
        RenderResolveSyncMethod();
        AppendLine();
        RenderFindByIdentityKeyMethod();
        AppendLine();
        RenderGetProfileMethod();
        AppendLine();
        RenderGetProfileSyncMethod();

        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderResolveMethod()
    {
        XmlSummary("Resolves the user entity for the current authenticated user.");
        AppendLine($"public async Task<{_model.TypeName}?> ResolveAsync(CancellationToken ct = default)");
        AppendLine("{");
        IncreaseIndent();

        var predicate = RenderMatchPreamble("null");
        // Query(), whose own remark reserves it for infrastructure that cannot use a specification:
        // this needs a synchronous path, because II18NConfigProvider.GetConfiguration() is synchronous.
        AppendLine("return await repository.Query()");
        AppendLine($"    .FirstOrDefaultAsync(u => {predicate}, ct)");
        AppendLine("    .ConfigureAwait(false);");

        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     The same lookup, synchronously, for the callers that have no asynchronous seam to use.
    /// </summary>
    /// <remarks>
    ///     A genuine synchronous EF Core query, not <c>.GetAwaiter().GetResult()</c> over the
    ///     asynchronous one: blocking a thread on a query the provider issues is a cost, blocking it on
    ///     a task scheduled to the same pool is a way to run out of threads.
    /// </remarks>
    private void RenderResolveSyncMethod()
    {
        XmlSummary(
            "Resolves the user entity synchronously, for callers on a synchronous contract " +
            "(<see cref=\"global::Pragmatic.Internationalization.Context.II18NConfigProvider\"/> is one). " +
            "Prefer ResolveAsync everywhere else.");
        AppendLine($"public {_model.TypeName}? Resolve()");
        AppendLine("{");
        IncreaseIndent();

        var predicate = RenderMatchPreamble("null");
        AppendLine("return repository.Query()");
        AppendLine($"    .FirstOrDefault(u => {predicate});");

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
        AppendLine($"public async Task<{_model.TypeName}?> FindByIdentityKeyAsync(string identityKey, CancellationToken ct = default)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("if (string.IsNullOrEmpty(identityKey)) return null;");
        AppendLine();
        AppendLine("return await repository.Query()");
        AppendLine($"    .FirstOrDefaultAsync(u => u.{_model.MatchProperty} == identityKey, ct)");
        AppendLine("    .ConfigureAwait(false);");
        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     Emits the guards that turn <c>ICurrentUser</c> into the value the query matches on, and
    ///     returns the predicate body.
    /// </summary>
    /// <remarks>
    ///     Shared by both overloads on purpose. Two copies of "which claim identifies the user" is the
    ///     shape where one of them keeps working and the other silently matches nobody.
    /// </remarks>
    private string RenderMatchPreamble(string noMatchResult)
    {
        AppendLine($"if (!currentUser.IsAuthenticated) return {noMatchResult};");
        AppendLine();

        var matchExpr = $"u.{_model.MatchProperty}";
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

            return $"{matchExpr} == key";
        }

        AppendLine($"var claimValues = currentUser.Claims.GetValueOrDefault(\"{_model.MatchClaim}\");");
        AppendLine($"if (claimValues is null || claimValues.Count == 0) return {noMatchResult};");
        AppendLine("var claimValue = claimValues[0];");
        AppendLine();

        return $"{matchExpr} == claimValue";
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

    private void RenderGetProfileSyncMethod()
    {
        XmlSummary("Resolves the user and returns their profile synchronously, or null if not found.");
        AppendLine("public IUserProfile? GetProfile()");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("var user = Resolve();");
        AppendLine("return user?.ToProfile();");
        DecreaseIndent();
        AppendLine("}");
    }
}
