---
title: "Common Mistakes"
description: "Wrong: delaying adoption because the Agent is not wired yet."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Gateway/docs/common-mistakes.md
sidebar:
  order: 3
---
### 1. Assuming the gateway only works with the Agent

Wrong: delaying adoption because the Agent is not wired yet.

Right: use static routes first and add Agent-backed routing later.

### 2. Forgetting that static routes are fallback

If Agent-provided routes use the same route ids, they take precedence. Do not rely on static config winning against a matching dynamic route.

### 3. Applying global assumptions to resilience

Resilience is per cluster. A circuit opening for one backend does not mean the entire gateway is down.

### 4. Misordering operational expectations

Maintenance is checked before auth in the runtime pipeline. That is intentional. Do not assume requests will always be authenticated before maintenance can short-circuit them.

### 5. Treating it as documentation-only preview code

The gateway executable is implemented. The preview caveat is about evolution and packaging, not about the absence of working runtime behavior.

