# pragmatic-design: agent skills plugin

Skills for building **line-of-business** applications with the **Pragmatic.Design** NuGet
packages on .NET 10.

Pragmatic.Design is an **attribute-first, code-generation** .NET ecosystem: you decorate classes
and properties with attributes, and a unified source generator produces entities, repositories,
queries, invokers, endpoints, and DI registrations. These skills teach an agent *what* to
decorate, *what* the generator produces, and *how* to consume it, along with project-structuring
best practices.

## Recommended flow

1. `pragmatic-new-app`: scaffold the host and the first boundary library.
2. `pragmatic-architecture`: split into modules, boundaries, databases.
3. `pragmatic-choose-modules`: select the packages for each feature.
4. `pragmatic-use-persistence`, `pragmatic-use-actions-endpoints`: domain modeling.
5. The other `pragmatic-use-*` skills, one per feature the application takes up.

The packages are on nuget.org as prereleases (`dotnet add package … --prerelease`).
`pragmatic-nuget-feed` is only for a restore that fails, or for consuming builds that are not on
nuget.org yet through a local feed.

`pragmatic-ecosystem` is the hub: it holds the reference index (`references/`) and the blessed
patterns map.

## Structure

```
skills/
├── pragmatic-ecosystem/
│   ├── SKILL.md
│   └── references/            # packages, patterns-map, diagnostics, api-surface, cookbook
├── pragmatic-architecture/
├── pragmatic-choose-modules/
├── pragmatic-new-app/
├── pragmatic-nuget-feed/
└── pragmatic-use-*/           # one skill per feature module
```

The folder list under `skills/` is the complete set.

The `pragmatic-use-*` skills point to the dense references under
`pragmatic-ecosystem/references/` via plugin-internal relative paths, so the folders are installed
together: copying one skill without `pragmatic-ecosystem` leaves those links pointing at nothing.

## Notes

- Target for consumer projects: `.NET 10`, `PackageReference` (never `ProjectReference`).
- The skills cite the Pragmatic.Design repository examples (`examples/showcase/`) as an optional
  reference: useful only if you have access to that repository, never required to use the skills.
- Version aligned with `.claude-plugin/plugin.json`. Codex reads the same manifest.
