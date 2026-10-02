using System.Reflection;

namespace Pragmatic.Configuration.Management.Tests.Unit;

/// <summary>
///     Test helper that sets the private dependency fields that the source-generated
///     invoker would normally populate (e.g. <c>_store</c>, <c>_auditStore</c>),
///     so action bodies can be exercised directly in isolation.
/// </summary>
internal static class ActionFieldInjector
{
    public static T Inject<T>(T action, string fieldName, object? value)
    {
        var field = typeof(T).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Field '{fieldName}' not found on {typeof(T).Name}.");

        field.SetValue(action, value);
        return action;
    }
}
