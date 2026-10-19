using Pragmatic.Persistence.Query.Attributes;

namespace Pragmatic.Persistence.Fuzz;

/// <summary>The adapter the generator writes for <see cref="FuzzOrder" />, for both grid formats.</summary>
[GridAdapter<FuzzOrder>(Framework = GridFramework.Both)]
public partial class FuzzOrderGrid;
