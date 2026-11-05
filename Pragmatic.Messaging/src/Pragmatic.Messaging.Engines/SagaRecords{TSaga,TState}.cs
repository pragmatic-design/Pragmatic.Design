using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Messaging.Entities;
using Pragmatic.Messaging.Saga;
using Pragmatic.Serialization;

namespace Pragmatic.Messaging.Engines;

/// <summary>
///     A saga to and from the instance record of either engine, as <c>EfCoreSagaRepository</c> converts it: the state by number, the saga as JSON
///     through the application's <see cref="PragmaticJsonOptions" /> (its generated context, so AOT-clean), the core members read back from the
///     record over the JSON's; and the outbox messages a step leaves, as that repository writes them.
/// </summary>
internal sealed class SagaRecords<TSaga, TState>(PragmaticJsonOptions? jsonOptions)
    where TSaga : class, ISaga<TState>, new()
    where TState : struct, Enum
{
    /// <summary>The saga's type, as the record names it: rows of every saga type share the table.</summary>
    public static readonly string SagaType = typeof(TSaga).FullName ?? typeof(TSaga).Name;

    private readonly JsonSerializerOptions _json = (jsonOptions ?? PragmaticJsonOptions.Default).Build();

    private JsonTypeInfo<TSaga> SagaTypeInfo => (JsonTypeInfo<TSaga>)_json.GetTypeInfo(typeof(TSaga));

    /// <summary><paramref name="record" />, a new instance of <paramref name="saga" /> owned by <paramref name="tenantId" />.</summary>
    public T New<T>(T record, TSaga saga, string tenantId, DateTimeOffset now) where T : ISagaInstanceRecord
    {
        record.Id = saga.Id;
        record.SagaType = SagaType;
        record.CorrelationId = saga.CorrelationId;
        record.StartedAt = saga.StartedAt;
        record.TenantId = tenantId;
        record.Version = 0;
        Apply(record, saga, now);
        return record;
    }

    /// <summary>The saga's state, data, status and completion written to <paramref name="record" />, its last step stamped <paramref name="now" />.</summary>
    public void Apply(ISagaInstanceRecord record, TSaga saga, DateTimeOffset now)
    {
        record.State = ToInt(saga.State);
        record.StateData = JsonSerializer.Serialize(saga, SagaTypeInfo);
        record.Status = (int)(saga.CompletedAt.HasValue ? SagaStatus.Completed : SagaStatus.Active);
        record.CompletedAt = saga.CompletedAt;
        record.LastStepAt = now;
    }

    /// <summary>The saga <paramref name="record" /> holds: its data, with the core members the record's.</summary>
    public TSaga Read(ISagaInstanceRecord record)
    {
        var saga = string.IsNullOrEmpty(record.StateData) ? new TSaga() : JsonSerializer.Deserialize(record.StateData, SagaTypeInfo)!;
        saga.Id = record.Id;
        saga.CorrelationId = record.CorrelationId;
        saga.State = FromInt(record.State);
        saga.StartedAt = record.StartedAt;
        saga.CompletedAt = record.CompletedAt;
        return saga;
    }

    /// <summary>An executed step of instance <paramref name="sagaInstanceId" />, completed at <paramref name="now" />.</summary>
    public static T Step<T>(T record, Guid sagaInstanceId, string stepName, DateTimeOffset now) where T : ISagaStepRecord
    {
        record.Id = Guid.NewGuid();
        record.SagaInstanceId = sagaInstanceId;
        record.StepName = stepName;
        record.Status = (int)SagaStepStatus.Completed;
        record.StartedAt = now;
        record.CompletedAt = now;
        return record;
    }

    /// <summary>The outbox messages of a step's <paramref name="pending" /> messages: serialized through the generated context, pending.</summary>
    public List<OutboxMessage> Outbox(IReadOnlyList<object> pending, string correlationId, string? tenantId, DateTimeOffset now)
    {
        var messages = new List<OutboxMessage>(pending.Count);
        foreach (var message in pending)
        {
            var type = message.GetType();
            messages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                MessageType = type.FullName ?? type.Name,
                Payload = JsonSerializer.Serialize(message, _json.GetTypeInfo(type)),
                CreatedAt = now,
                CorrelationId = correlationId,
                TenantId = tenantId,
            });
        }

        return messages;
    }

    private static int ToInt(TState state) => (int)(object)state;

    private static TState FromInt(int value) => (TState)(object)value;
}
