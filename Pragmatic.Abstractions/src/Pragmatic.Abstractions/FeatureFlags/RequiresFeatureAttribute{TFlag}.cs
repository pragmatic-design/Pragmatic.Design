namespace Pragmatic.FeatureFlags;

/// <summary>
///     <see cref="RequiresFeatureAttribute" /> naming the flag by its <see cref="IFeatureFlag" /> type, so a
///     renamed flag is a compile error rather than a gate that never opens.
/// </summary>
/// <typeparam name="TFlag">The flag.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresFeatureAttribute<TFlag> : Attribute
    where TFlag : IFeatureFlag;
