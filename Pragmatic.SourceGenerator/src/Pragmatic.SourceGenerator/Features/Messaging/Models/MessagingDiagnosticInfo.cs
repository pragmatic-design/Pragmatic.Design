using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Messaging.Models;

/// <summary>Which shape diagnostic a <see cref="MessagingDiagnosticInfo"/> carries.</summary>
internal enum MessagingDiagnosticKind
{
    /// <summary>PRAG0800 — [MessageHandler] on a class that does not implement IMessageHandler&lt;T&gt;.</summary>
    HandlerMustImplementInterface,

    /// <summary>PRAG0803 — [MessageMiddleware] on a class that does not implement IMessageMiddleware.</summary>
    MiddlewareMustImplementInterface,

    /// <summary>PRAG0813 — [Saga&lt;T&gt;] where T is not an enum.</summary>
    SagaStateNotEnum,

    /// <summary>PRAG0831 — [EnableOutbox] without a Pragmatic.Messaging.EFCore reference.</summary>
    EnableOutboxWithoutEfCore,

    /// <summary>PRAG0836 — [PublicEvent] on a type that does not implement IDomainEvent.</summary>
    PublicEventOnNonDomainEvent,
}

/// <summary>
///     Value-equatable carrier for a messaging shape diagnostic surfaced by a dedicated FAWMN provider,
///     independent of the model-generation transforms (which stay null-returning for invalid triggers).
///     Uses <see cref="LocationInfo"/> and <see cref="EquatableArray{T}"/> so it caches correctly in the
///     incremental pipeline.
/// </summary>
internal sealed record MessagingDiagnosticInfo(
    MessagingDiagnosticKind Kind,
    LocationInfo? Location,
    EquatableArray<string> Args);
