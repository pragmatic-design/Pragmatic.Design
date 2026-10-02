---
title: "Troubleshooting"
description: "Check:"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Gateway/docs/troubleshooting.md
sidebar:
  order: 4
---
## Routes do not load from the Agent

Check:

- Agent socket path
- whether the Agent is running
- whether the expected `gateway/routes/` and `gateway/clusters/` keys exist

If the Agent is unavailable, verify that static routes are configured so the gateway still has a fallback route table.

## Requests always return maintenance responses

Inspect the maintenance state source first. If the gateway is connected to the Agent, maintenance may be coming from shared coordination rather than local configuration.

## One backend keeps failing but others work

That usually points to cluster-local resilience behavior, not a full gateway outage. Inspect timeout and circuit-breaker settings for the affected cluster.

## JWT authentication does not activate

JWT middleware is only wired when `GatewayOptions.Jwt` is configured. If the section is absent, the gateway will not add the authentication pipeline.

