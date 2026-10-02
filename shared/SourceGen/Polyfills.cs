// =============================================================================
// Pragmatic.Design - Polyfills for netstandard2.0
// Required for using modern C# features in source generators
// =============================================================================

#if NETSTANDARD2_0
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>
    ///     Required for init-only properties in netstandard2.0.
    /// </summary>
    internal static class IsExternalInit
    {
    }

    /// <summary>
    ///     Required for 'required' keyword in netstandard2.0.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property,
        Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute
    {
    }

    /// <summary>
    ///     Required for 'required' keyword in netstandard2.0.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute
    {
        public string FeatureName { get; } = featureName;
        public bool IsOptional { get; init; }
    }

    /// <summary>
    ///     Required for collection-expression builder types (e.g. EquatableArray) in netstandard2.0.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    internal sealed class CollectionBuilderAttribute(Type builderType, string methodName) : Attribute
    {
        public Type BuilderType { get; } = builderType;
        public string MethodName { get; } = methodName;
    }
}

// ReSharper disable once CheckNamespace
namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    ///     Required for 'required' keyword in netstandard2.0.
    /// </summary>
    [AttributeUsage(AttributeTargets.Constructor)]
    internal sealed class SetsRequiredMembersAttribute : Attribute
    {
    }
}

#endif