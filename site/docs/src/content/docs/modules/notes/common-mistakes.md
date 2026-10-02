---
title: "Common Mistakes"
description: "Use `Pragmatic.Comments` for public or customer-facing conversation. Notes are meant for internal annotations."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Notes/docs/common-mistakes.md
sidebar:
  order: 3
---
### 1. Using notes for customer-visible discussion

Use `Pragmatic.Comments` for public or customer-facing conversation. Notes are meant for internal annotations.

### 2. Missing `[Entity]`

The generator needs the parent id type to create the child note entity correctly.

### 3. Missing `partial`

The trait adds generated members to the parent entity, so the parent class must be partial.

### 4. Expecting anonymous authors

Notes are internal and always require an author identity.

### 5. Forgetting the edit window

If `EditWindowMinutes` is set, updates can be rejected after the configured period.

