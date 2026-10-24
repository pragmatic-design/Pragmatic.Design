using Pragmatic.Persistence.Query.Attributes;
using PragmaticDocumentSamples.Entities;

namespace PragmaticDocumentSamples.Queries;

/// <summary>A DevExpress grid over reviews: every scalar the class declares.</summary>
[GridAdapter<Review>(Framework = GridFramework.DevExpress)]
public partial class ReviewGrid;
