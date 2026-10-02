namespace Pragmatic.Jobs;

/// <summary>
///     One assembly's generated job-type registry, as it is registered for the composite to find.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>It exists to keep the composite out of its own enumerable.</b> A registry that fans
///         out over <c>IEnumerable&lt;IJobTypeRegistry&gt;</c> and is itself registered as
///         <c>IJobTypeRegistry</c> would be handed itself and recurse; the container cannot exclude a
///         service from the enumerable it builds. So the contributed registries are named by this
///         interface and the one the runtime consumes stays <see cref="IJobTypeRegistry" /> — one type
///         more, and every call site of the three services unchanged.
///     </para>
///     <para>
///         <b>Why there is more than one at all.</b> The generator emits a registry per assembly. If
///         each registration <c>Replace</c>d the previous one, two assemblies declaring jobs would cancel
///         each other out, and a job type shipped by a package — the messaging bridge's
///         <c>PublishMessageJob</c> — could never be executed at all. The runner would answer
///         "Unknown job type", after the message it was carrying had already been acknowledged.
///     </para>
/// </remarks>
public interface IJobTypeRegistrySource : IJobTypeRegistry;
