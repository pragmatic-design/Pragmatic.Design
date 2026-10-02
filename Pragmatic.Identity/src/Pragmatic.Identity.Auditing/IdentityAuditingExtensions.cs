using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Events;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Privacy;

namespace Pragmatic.Identity.Auditing;

/// <summary>
///     Turns on auditing of identity security events.
/// </summary>
public static class IdentityAuditingExtensions
{
    /// <summary>
    ///     Records failed logins and account lockouts on the framework audit trail, attributing them
    ///     through the application's own <typeparamref name="TLocator" />.
    /// </summary>
    /// <typeparam name="TLocator">
    ///     How this application turns the identity a security event named into the subject key its
    ///     registry holds — for an application whose subjects are employees, "the employee whose work
    ///     address is this".
    /// </typeparam>
    /// <remarks>
    ///     <para>
    ///         This is the overload to use whenever the application registers its subjects under its
    ///         own type, which <c>[DataSubject]</c> encourages: the parameterless one falls back to the
    ///         fixed pair <c>("User", &lt;identity&gt;)</c>, and where that does not match, every entry
    ///         is written with no subject and per-subject correlation finds nothing without saying so.
    ///     </para>
    ///     <para>
    ///         The trail is not registered here: call <c>AddAuditTrail()</c> from
    ///         <c>Pragmatic.Audit.EFCore</c>. Nor is the subject registry: call
    ///         <c>AddSubjectRegistry()</c>.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddIdentitySecurityAuditing<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
        TLocator>(this IServiceCollection services)
        where TLocator : class, ISecuritySubjectLocator
    {
        ArgumentNullException.ThrowIfNull(services);

        // Not TryAdd: naming a locator is choosing one, and silently keeping an earlier registration
        // would be the same silence this overload exists to end.
        services.AddScoped<ISecuritySubjectLocator, TLocator>();

        return services.AddIdentitySecurityAuditing();
    }

    /// <summary>
    ///     Records failed logins and account lockouts on the framework audit trail.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Attribution falls back to the pair <c>("User", &lt;identity&gt;)</c></b> — see
    ///         <see cref="LoginIdentityIsTheSubjectKey" />. That is right for an application whose
    ///         subject registry is keyed by the login address and wrong for every other, and when it is
    ///         wrong nothing says so: the entries are still written, correctly, with no subject.
    ///         <see cref="AddIdentitySecurityAuditing{TLocator}" /> is how an application answers for
    ///         itself.
    ///     </para>
    ///     <para>
    ///         The trail is not registered here: call <c>AddAuditTrail()</c> from
    ///         <c>Pragmatic.Audit.EFCore</c>. Nor is the subject registry: call
    ///         <c>AddSubjectRegistry()</c>. Without a registry every entry is written with no subject —
    ///         which is a working, if blunter, configuration rather than a broken one.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddIdentitySecurityAuditing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ObservedIdentityResolver>();
        services.TryAddScoped<ISecuritySubjectLocator, LoginIdentityIsTheSubjectKey>();

        // Enumerable: contributing a handler is adding a registration, never replacing whatever else
        // already listens to these events.
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IDomainEventHandler<LoginFailed>, LoginFailedAuditHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IDomainEventHandler<AccountLocked>, AccountLockedAuditHandler>());

        return services;
    }
}
