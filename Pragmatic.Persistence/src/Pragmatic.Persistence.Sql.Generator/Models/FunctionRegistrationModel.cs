using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A <c>[SqlFunction]</c> registered for the runtime path: the method, taken from a delegate, and its renderer per dialect.</summary>
internal sealed record FunctionRegistrationModel(
    string ContainingType,
    string Name,
    EquatableArray<string> ParameterTypes,
    string ReturnType,
    EquatableArray<FunctionArmModel> Arms,
    bool IsPredicate);
