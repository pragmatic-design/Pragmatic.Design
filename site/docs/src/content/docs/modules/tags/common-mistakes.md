---
title: "Common Mistakes"
description: "This trait is intentionally relational. It is designed for normalized tag workflows, not for embedding arrays of strings into one parent table field."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Tags/docs/common-mistakes.md
sidebar:
  order: 3
---
### 1. Treating tags as a free-form JSON column

This trait is intentionally relational. It is designed for normalized tag workflows, not for embedding arrays of strings into one parent table field.

### 2. Using the wrong generic id type for `ITagPolicy<TEntityId>`

The policy generic must match the parent entity id type.

### 3. Forgetting that scope changes identity

Two tags with the same value but different scopes are distinct tag sets.

### 4. Forgetting the case-sensitivity setting

If `CaseSensitive` is false, `Urgent` and `urgent` collapse to the same normalized tag.

### 5. Setting `AllowCustom = false` and expecting on-the-fly creation

When custom tags are disabled, the workflow must use pre-created tags from the allowed taxonomy.

