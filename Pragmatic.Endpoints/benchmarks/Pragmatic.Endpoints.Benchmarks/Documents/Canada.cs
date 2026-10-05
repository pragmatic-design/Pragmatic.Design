// Copied from RE:Dox (https://github.com/CAPCOM-TD-OSS/redox, benchmarks/REDox.Benchmarks/Data/Canada.cs),
// Copyright (c) CAPCOM CO.,LTD., licensed under the Apache License, Version 2.0
// (https://www.apache.org/licenses/LICENSE-2.0). Changed: the namespace, and the [DataSource] attribute
// of their harness removed. The member names match the JSON documents, which is why they are lowercase.
namespace Pragmatic.Endpoints.Benchmarks.Documents;

public sealed class Canada
{
    public sealed class Root
    {
        public string? type { get; set; }

        public Feature[]? features { get; set; }
    }

    public sealed class Feature
    {
        public string? type { get; set; }

        public FeatureProperties? properties { get; set; }

        public Geometry? geometry { get; set; }
    }

    public sealed class FeatureProperties
    {
        public string? name { get; set; }
    }

    public sealed class Geometry
    {
        public string? type { get; set; }

        public double[][][]? coordinates { get; set; }
    }
}