using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>A DevExpress grid over reviews.</summary>
[Pragmatic.Persistence.Query.Attributes.GridAdapter<Review>(Framework = Pragmatic.Persistence.Query.Attributes.GridFramework.DevExpress)]
public partial class SmokeReviewGrid;
