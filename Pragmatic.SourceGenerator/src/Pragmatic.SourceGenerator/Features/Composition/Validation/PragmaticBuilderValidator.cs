using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Diagnostics;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Validation;

/// <summary>
///     Validates module dependency rules at compile time.
///     Reports diagnostics when referenced modules have unmet dependencies.
/// </summary>
internal static class PragmaticBuilderValidator
{
    /// <param name="context">Where the diagnostics go.</param>
    /// <param name="features">What this host's compilation can name.</param>
    /// <param name="hostModules">The host's own <c>[Module]</c> declarations.</param>
    /// <param name="somethingTakesTheClock">
    ///     Whether any assembly of this application declares a <c>[FromClock]</c> — read from the
    ///     metadata the modules emit, because the declaration is somebody else's syntax and the
    ///     registration is the host's business (PRAG1698).
    /// </param>
    /// <param name="somethingTakesTheTenant">
    ///     Whether any assembly of this application declares a <c>[FromTenant]</c>, read the same way
    ///     (PRAG1615).
    /// </param>
    /// <param name="somethingGatesAFeature">
    ///     Whether any assembly of this application declares a <c>[RequiresFeature]</c> (PRAG1616).
    /// </param>
    public static void Validate(
        SourceProductionContext context,
        DetectedFeatures features,
        ImmutableArray<ModuleModel> hostModules,
        bool somethingTakesTheClock = false,
        bool somethingTakesTheTenant = false,
        bool somethingGatesAFeature = false)
    {
        // PRAG1616: an operation is refused when a feature flag is off, and this host cannot evaluate one,
        // so the gate is shut for everyone.
        if (somethingGatesAFeature && !features.HasFeatureFlags)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.FeatureGateWithoutFeatureFlags,
                Location.None));
        }

        // PRAG1615: an operation takes the request's tenant and this host cannot resolve one, so every
        // call to it reads no tenant.
        if (somethingTakesTheTenant && !features.HasMultiTenancy)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.TenantBindingWithoutMultiTenancy,
                Location.None));
        }

        // PRAG1698: an operation takes a value from the clock and this host cannot name one. The host
        // registers AddPragmaticTemporal() whenever the package is there, so the only case left is the
        // one where it is not — and there, saying so is all a generator can do.
        if (somethingTakesTheClock && !features.HasTemporal)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.ClockBindingWithoutAClock,
                Location.None));
        }

        // PRAG1695: Authorization without Identity, unless the host declares it has no authentication
        var anonymousHost = !hostModules.IsDefaultOrEmpty && hostModules.Any(m => m.IsAnonymousHost);
        if (features.HasAuthorization && !features.HasIdentityAspNetCore && !anonymousHost)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.AuthorizationWithoutIdentity,
                Location.None));
        }

        // PRAG1696: Identity.Persistence without Authorization
        if (features.HasIdentityPersistence && !features.HasAuthorization)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.IdentityPersistenceWithoutAuthorization,
                Location.None));
        }
    }
}
