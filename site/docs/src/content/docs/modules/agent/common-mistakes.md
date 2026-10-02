---
title: "Common Mistakes"
description: "Wrong: treating the module as unimplemented and avoiding it entirely."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Agent/docs/common-mistakes.md
sidebar:
  order: 3
---
### 1. Assuming the Agent is only a design stub

Wrong: treating the module as unimplemented and avoiding it entirely.

Right: treat it as an implemented preview subsystem whose runtime exists today, while keeping in mind that packaging and operational guidance are still evolving.

### 2. Using the wrong socket path

Wrong:

```csharp
builder.UseAgent(agent => agent.SocketPath = "/tmp/agent.sock");
```

when the daemon is listening on `/var/run/pragmatic/agent.sock`.

Right: keep the app and daemon on the same socket or pipe name.

### 3. Treating auto-start as a production deployment strategy

`AgentAutoStart` is a development convenience. In production, start and supervise the daemon explicitly.

### 4. Assuming the daemon is already AOT-published

The runtime exists, but the daemon project does not currently ship with AOT enabled. Do not document or operationalize it as if Native AOT were already the default.

### 5. Forgetting that graceful degradation is intentional

If the Agent is unreachable, the app may keep serving with fallback behavior. That is by design. Make sure your operational checks verify whether you are connected to the Agent when coordination matters.

