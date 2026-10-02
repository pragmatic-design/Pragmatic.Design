---
title: "Troubleshooting"
description: "Check:"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Notes/docs/troubleshooting.md
sidebar:
  order: 4
---
## Notes are not generated

Check:

- `partial` on the parent entity
- `[Entity]`
- the package and source generator references in the consuming project

## Update requests are rejected

Inspect:

- `AllowEditing`
- `EditWindowMinutes`
- your authorization and caller identity setup

## Endpoints are missing

If the entity artifacts exist but routes do not, confirm the parent resource metadata and endpoint package integration in the consuming project.

