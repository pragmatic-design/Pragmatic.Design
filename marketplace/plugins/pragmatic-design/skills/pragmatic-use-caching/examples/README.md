# Examples — pragmatic-use-caching

Copied from `examples/showcase/src`, which compiles in the repository and is exercised by
`examples/showcase/tests/Showcase.IntegrationTests` and `examples/showcase/tests/Showcase.Host.Distributed.Tests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`Showcase.Catalog/Amenities/Queries/SearchAmenitiesQuery.cs`](Showcase.Catalog/Amenities/Queries/SearchAmenitiesQuery.cs) | A cacheable query: `[Cacheable]` with a duration and a tag, every property in the key |
| [`Showcase.Catalog/Amenities/Mutations/CreateAmenityMutation.cs`](Showcase.Catalog/Amenities/Mutations/CreateAmenityMutation.cs) | The write that drops that tag: `[InvalidatesCache("amenities")]` on a mutation |
| [`Showcase.Catalog/Properties/Queries/SearchPropertiesQuery.cs`](Showcase.Catalog/Properties/Queries/SearchPropertiesQuery.cs) | `[CacheKey]` naming and ordering the key's parts on a search with many filters — the default key was 1 200 characters |
| [`Showcase.Catalog/Properties/Actions/ReactivatePropertiesInCityAction.cs`](Showcase.Catalog/Properties/Actions/ReactivatePropertiesInCityAction.cs) | `[InvalidatesCache]` on a domain action that writes rows a cached search answers with |
| [`Showcase.Host.Distributed/Program.cs`](Showcase.Host.Distributed/Program.cs) | Two hosts, one answer: `AddRedisCacheInvalidationBroadcast` carries an invalidation to the other host's in-process copy |
