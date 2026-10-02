---
title: "Concepts"
description: "`Pragmatic.Notes` is the internal-annotation trait."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Notes/docs/concepts.md
sidebar:
  order: 1
---
`Pragmatic.Notes` is the internal-annotation trait.

## Mental Model

Use notes when staff or operators need private annotations on a domain entity:

- reservation handling notes
- fraud review remarks
- internal billing comments

Notes are intentionally simpler than comments:

- no threading
- no anonymous authors
- no public visibility model

## Generated Shape

From `[HasNotes]`, the consuming project gets:

- a typed `{Parent}Note` entity
- EF configuration and FK
- actions for add, get-by-id, update, and delete
- note endpoints under the parent resource
- a `Notes` navigation on the parent

