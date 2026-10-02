# Troubleshooting

## Duplicate-looking tags appear or disappear

Check:

- `CaseSensitive`
- `Scope`
- the normalization performed by your `ITagPolicy<TEntityId>`

## Add tag is rejected

Inspect:

- `MaxPerEntity`
- `AllowCustom`
- custom policy validation

## No endpoints are generated

As with the other trait packages, verify the parent entity metadata and the endpoint package integration in the consuming project.

## List results look empty

The usual causes are scope mismatch, querying the wrong parent entity, or using a different boundary than the one where the tag entity was generated.

