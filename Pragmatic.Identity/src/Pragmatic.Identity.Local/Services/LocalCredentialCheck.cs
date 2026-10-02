using Microsoft.Extensions.Logging;
using Pragmatic.Events;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     The check of local credentials that <c>LoginUser</c> and <c>SignInUser</c> share: the account, its
///     state, its lockout and its password, with the counters and events that go with each answer.
/// </summary>
/// <remarks>
///     A helper both operations call rather than one operation invoking the other: an action of this
///     package belongs to no boundary, so it has no unit of work to compose another action in, and the
///     check's own writes — a failed attempt counted, a lockout started — have to stay written whatever the
///     caller answers.
/// </remarks>
internal static partial class LocalCredentialCheck
{
    /// <summary>
    ///     The identity whose credentials these are, with its counters reset and <c>LastLoginAt</c> set;
    ///     or the refusal, with the failed attempt recorded.
    /// </summary>
    public static async Task<Result<LocalIdentity, IError>> VerifyAsync(
        string email,
        string password,
        ILocalIdentityStore store,
        IPasswordHasher hasher,
        LocalIdentityOptions opts,
        IClock clock,
        IDomainEventDispatcher events,
        ILogger logger,
        CancellationToken ct)
    {
        var now = clock.UtcNow;

        var identity = await store.FindByEmailAsync(LocalIdentity.NormalizeEmail(email), ct).ConfigureAwait(false);
        if (identity is null)
        {
            // Run an equivalent-cost bcrypt operation on the not-found path so response timing does not
            // reveal whether the email exists (the found path runs the bcrypt verify below).
            _ = hasher.Hash(password);
            // Use the same generic reason string to avoid leaking email existence via audit logs.
            await events.DispatchAsync(new LoginFailed(email, "Invalid credentials", now), ct).ConfigureAwait(false);
            return new InvalidCredentialsError();
        }

        if (!identity.IsActive)
        {
            // Equalize timing: run the same bcrypt work as a live account even though we already deny,
            // so an inactive account is not measurably faster than a wrong-password reply.
            _ = hasher.Hash(password);
            // Use the same generic reason string to avoid leaking account existence via audit logs.
            await events.DispatchAsync(new LoginFailed(email, "Invalid credentials", now), ct).ConfigureAwait(false);
            // Uniform (default): indistinguishable from any other failure. Reveal: distinct 403.
            if (opts.RevealAccountState)
                return new IdentityNotActiveError();
            return new InvalidCredentialsError();
        }

        // Check lockout — still enforced internally in both modes; only the *disclosure* differs.
        if (identity.LockoutEnd.HasValue)
        {
            if (identity.LockoutEnd > now)
            {
                // Equalize timing on the locked path too.
                _ = hasher.Hash(password);
                await events.DispatchAsync(new LoginFailed(email, "Account locked", now), ct).ConfigureAwait(false);
                // Uniform (default): the lockout is not revealed via 423 — awareness comes from the
                // AccountLocked event/email and the login rate limiter. Reveal: distinct 423.
                if (opts.RevealAccountState)
                    return new AccountLockedError(identity.LockoutEnd);
                return new InvalidCredentialsError();
            }

            // Lockout has expired: clear the stale failed-attempt counter before evaluating the password,
            // otherwise a single subsequent failure would immediately re-lock the account.
            identity.FailedLoginAttempts = 0;
            identity.LockoutEnd = null;
        }

        // Verify password
        if (!hasher.Verify(password, identity.PasswordHash))
        {
            identity.FailedLoginAttempts++;

            if (identity.FailedLoginAttempts >= opts.MaxFailedLoginAttempts)
            {
                identity.LockoutEnd = now.Add(opts.LockoutDuration);
                await store.UpdateAsync(identity, ct).ConfigureAwait(false);
                await events.DispatchAsync(new AccountLocked(identity.ExternalIdentityKey, identity.LockoutEnd, now), ct).ConfigureAwait(false);
            }
            else
            {
                await store.UpdateAsync(identity, ct).ConfigureAwait(false);
            }

            await events.DispatchAsync(new LoginFailed(email, "Invalid password", now), ct).ConfigureAwait(false);
            return new InvalidCredentialsError();
        }

        // Credentials are valid — enforce email verification before issuing a session, so the gate
        // cannot be probed by an unauthenticated caller (only a correct password reaches this point).
        if (opts.RequireEmailVerification && !identity.EmailVerified)
        {
            await events.DispatchAsync(new LoginFailed(email, "Email not verified", now), ct).ConfigureAwait(false);
            // Uniform (default): deny with the generic 401 so an unverified-but-correct-password account
            // is not distinguishable from a wrong password. Reveal: distinct 403.
            if (opts.RevealAccountState)
                return new EmailNotVerifiedError();
            return new InvalidCredentialsError();
        }

        // Success — reset counters
        identity.FailedLoginAttempts = 0;
        identity.LockoutEnd = null;
        identity.LastLoginAt = now;

        // Transparent work-factor upgrade: if the stored hash predates a cost increase, re-hash the
        // (already-verified) password so the stronger hash is persisted by the state update below — a single
        // extra write is avoided. Best-effort: a hashing failure must never block an otherwise-valid login,
        // so it is swallowed and logged, leaving the old (still-valid) hash in place.
        try
        {
            if (hasher.NeedsRehash(identity.PasswordHash))
                identity.PasswordHash = hasher.Hash(password);
        }
        catch (Exception ex)
        {
            LogPasswordRehashFailed(logger, ex);
        }

        await store.UpdateAsync(identity, ct).ConfigureAwait(false);

        await events.DispatchAsync(new UserLoggedIn(identity.ExternalIdentityKey, now, now), ct).ConfigureAwait(false);

        return identity;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Password hash work-factor upgrade failed on login; the existing hash is retained.")]
    private static partial void LogPasswordRehashFailed(ILogger logger, Exception exception);
}
