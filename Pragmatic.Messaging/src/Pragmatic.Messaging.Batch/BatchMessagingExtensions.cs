using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Messaging.Configuration;

namespace Pragmatic.Messaging.Batch;

/// <summary>
///     Extension methods for registering batch processing on <see cref="MessagingBuilder"/>.
/// </summary>
public static class BatchMessagingExtensions
{
    extension(MessagingBuilder builder)
    {
        /// <summary>
        ///     Enables batch processing with <see cref="BatchDispatcher{TBatch,TItem}"/>
        ///     and in-memory progress tracking.
        /// </summary>
        public MessagingBuilder EnableBatchProcessing()
        {
            builder.Services.TryAddSingleton<IBatchProgressStore, InMemoryBatchProgressStore>();
            builder.Services.AddScoped(typeof(BatchDispatcher<,>));
            builder.Services.AddScoped(typeof(IBatchDispatcher<,>), typeof(BatchDispatcher<,>));
            // Auto-reports each item's outcome so a batch cannot stay "active" forever because a
            // handler threw (or was dead-lettered) without reporting. Handlers must therefore NOT
            // also call BatchTracker.Report*Async — see BatchProgressMiddleware.
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IMessageMiddleware, BatchProgressMiddleware>());
            // One per message scope, shared by the handler (which may fail its item) and the middleware.
            builder.Services.TryAddScoped<BatchItemOutcome>();
            return builder;
        }

        // EF-backed batch progress is provisioned by marking a [Boundary] with [EnableBatchProgress]:
        // the source generator maps the __BatchProgress table into that boundary's DbContext and
        // registers EfCoreBatchProgressStore against it (see BatchProgressExtensions.AddBatchProgress).
        // There is no runtime switch for it: swapping the store at runtime provisions no table.
    }
}
