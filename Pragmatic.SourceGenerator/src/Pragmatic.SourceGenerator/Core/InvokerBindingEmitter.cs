using System;
using System.Collections.Generic;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The statements that write the <c>[FromCurrentUser]</c> and <c>[FromClock]</c> properties of an
///     operation — a query, an action or a mutation — before it runs.
/// </summary>
/// <remarks>
///     <para>
///         Three invokers emit them, and they differ only in the names at the emission site: the
///         operation's variable, the provider, the token, and how a refusal is returned. Those are
///         parameters and the rest is said once, so an attribute cannot mean one thing on a query and
///         another on a mutation.
///     </para>
///     <para>
///         Every invoker emits them at the same point of its base's order: validation and authorization
///         have run, nothing has been read. The locals are prefixed because the preparation hook of an
///         action also declares one variable per <c>[LoadEntity]</c>, named after the entity — and the
///         user entity is the one an operation is most likely to load.
///     </para>
///     <para>
///         ⚠️ <b>The resolver is constructed, not resolved.</b> This generator writes it, in this
///         compilation, with a constructor it knows — the read repository and the current user — so the
///         invoker needs nothing to have registered it. The current user is asked for the way the bases
///         ask for it, and a container without one reads as a caller who is not authenticated: the answer
///         is the same 401 either way.
///     </para>
///     <para>
///         ⚠️ <b>The clock is required, not looked for.</b> A host that has no <c>IClock</c> is
///         misconfigured, and a fallback to the wall clock here would be exactly the reading the attribute
///         exists to replace: a clock a test or a host injects would reach the writes and not this one.
///         Temporal registers one.
///     </para>
/// </remarks>
internal static class InvokerBindingEmitter
{
    /// <summary>Yields the statements to emit, in order; nothing when no binding is written.</summary>
    /// <param name="currentUser">The <c>[FromCurrentUser]</c> bindings, resolved.</param>
    /// <param name="clock">The <c>[FromClock]</c> bindings.</param>
    /// <param name="target">The variable holding the operation at the emission site.</param>
    /// <param name="services">The expression naming the <c>IServiceProvider</c>.</param>
    /// <param name="cancellation">The expression naming the cancellation token.</param>
    /// <param name="refuse">The statement that ends the invocation with an error, given the error.</param>
    public static IEnumerable<string> Statements(
        IEnumerable<CurrentUserBindingModel> currentUser,
        IEnumerable<ClockBindingModel> clock,
        string target,
        string services,
        string cancellation,
        Func<string, string> refuse)
    {
        var fromTheCaller = currentUser.Where(b => b.IsRendered).ToList();
        if (fromTheCaller.Count > 0)
        {
            yield return $"var __currentUser = {services}.GetService(typeof(global::Pragmatic.Identity.ICurrentUser))";
            yield return "    as global::Pragmatic.Identity.ICurrentUser ?? global::Pragmatic.Identity.AnonymousUser.Instance;";
            yield return "if (!__currentUser.IsAuthenticated)";
            yield return "    " + refuse("global::Pragmatic.Result.Http.UnauthorizedError.Create()");
            yield return "";

            // One resolution for every member bound: the entity is one row, whichever of its members is read.
            var user = fromTheCaller.FirstOrDefault(b => b.ResolverTypeFullName is not null);
            if (user is not null)
            {
                yield return $"var __user = await new {user.ResolverTypeFullName}(";
                yield return "    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions"
                             + ".GetRequiredService<global::Pragmatic.Persistence.Repository.IReadRepository<"
                             + $"{user.UserTypeFullName}>>({services}),";
                yield return "    __currentUser)";
                yield return $"    .ResolveAsync({cancellation}).ConfigureAwait(false);";
                yield return "if (__user is null)";
                yield return "    " + refuse(
                    $"global::Pragmatic.Result.Http.NotFoundError.For(\"{user.UserTypeName}\", __currentUser.Id)");
                yield return "";
            }

            foreach (var binding in fromTheCaller)
                yield return binding.Member is null
                    ? $"{target}.{binding.PropertyName} = __currentUser.Id;"
                    : $"{target}.{binding.PropertyName} = __user.{binding.Member};";

            yield return "";
        }

        var fromTheClock = clock.Where(b => b.IsRendered).ToList();
        if (fromTheClock.Count > 0)
        {
            yield return "var __clock = global::Microsoft.Extensions.DependencyInjection"
                         + ".ServiceProviderServiceExtensions.GetRequiredService<"
                         + $"global::Pragmatic.Temporal.Clock.IClock>({services});";

            foreach (var binding in fromTheClock)
                yield return $"{target}.{binding.PropertyName} = __clock.{binding.ClockMember};";

            yield return "";
        }
    }
}
