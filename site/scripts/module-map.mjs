// Shared module → slug/category map, consumed by sync-docs.mjs (site) and
// scripts/sync-slnx-docs.mjs (solution docs folders).
// Module slug mapping — maps directory name to docs slug
export const MODULE_MAP = {
  // Foundation
  "Pragmatic.Result": { slug: "result", category: "foundation", order: 1 },
  "Pragmatic.Ensure": { slug: "ensure", category: "foundation", order: 2 },
  "Pragmatic.Abstractions": { slug: "abstractions", category: "foundation", order: 3 },

  // Core Modules
  "Pragmatic.Endpoints": { slug: "endpoints", category: "core", order: 1 },
  "Pragmatic.Actions": { slug: "actions", category: "core", order: 2 },
  "Pragmatic.Persistence": { slug: "persistence", category: "core", order: 3 },
  "Pragmatic.Composition": { slug: "composition", category: "core", order: 4 },
  "Pragmatic.Events": { slug: "events", category: "core", order: 5 },
  "Pragmatic.Messaging": { slug: "messaging", category: "core", order: 6 },
  "Pragmatic.Client": { slug: "client", category: "core", order: 7 },

  // Capabilities
  "Pragmatic.Validation": { slug: "validation", category: "capabilities", order: 1 },
  "Pragmatic.Mapping": { slug: "mapping", category: "capabilities", order: 2 },
  "Pragmatic.Caching": { slug: "caching", category: "capabilities", order: 3 },
  "Pragmatic.Specification": { slug: "specification", category: "capabilities", order: 4 },
  "Pragmatic.Patch": { slug: "patch", category: "capabilities", order: 5 },
  "Pragmatic.Configuration": { slug: "configuration-module", category: "capabilities", order: 6 },
  "Pragmatic.Testing": { slug: "testing", category: "capabilities", order: 7 },

  // Infrastructure
  "Pragmatic.Identity": { slug: "identity", category: "infrastructure", order: 1 },
  "Pragmatic.Authorization": { slug: "authorization", category: "infrastructure", order: 2 },
  "Pragmatic.Resilience": { slug: "resilience", category: "infrastructure", order: 3 },
  "Pragmatic.Internationalization": { slug: "i18n", category: "infrastructure", order: 4 },
  "Pragmatic.Temporal": { slug: "temporal", category: "infrastructure", order: 5 },
  "Pragmatic.MultiTenancy": { slug: "multi-tenancy", category: "infrastructure", order: 6 },
  "Pragmatic.Storage": { slug: "storage", category: "infrastructure", order: 7 },
  "Pragmatic.Logging": { slug: "logging", category: "infrastructure", order: 8 },
  "Pragmatic.FeatureFlags": { slug: "feature-flags", category: "infrastructure", order: 9 },
  "Pragmatic.Discovery": { slug: "discovery", category: "infrastructure", order: 10 },
  "Pragmatic.Jobs": { slug: "jobs", category: "infrastructure", order: 11 },
  "Pragmatic.Migrations": { slug: "migrations", category: "infrastructure", order: 12 },
  // The control-plane contracts are part of Pragmatic.Abstractions (its docs/how-it-works/control-plane.md).

  // Compliance
  "Pragmatic.Privacy": { slug: "privacy", category: "compliance", order: 1 },
  "Pragmatic.Audit": { slug: "audit", category: "compliance", order: 2 },
  "Pragmatic.Redaction": { slug: "redaction", category: "compliance", order: 3 },
  "Pragmatic.Cryptography": { slug: "cryptography", category: "compliance", order: 4 },
  "Pragmatic.Incidents": { slug: "incidents", category: "compliance", order: 5 },

  // Documents & Media
  "Pragmatic.Documents": { slug: "documents", category: "documents-media", order: 1 },
  "Pragmatic.Imaging": { slug: "imaging", category: "documents-media", order: 2 },
  "Pragmatic.Email": { slug: "email", category: "documents-media", order: 3 },
  "Pragmatic.Notifications": { slug: "notifications", category: "documents-media", order: 4 },

  // Medium Blocks (traits + packages)
  "Pragmatic.Comments": { slug: "comments", category: "medium-blocks", order: 1 },
  "Pragmatic.Tags": { slug: "tags", category: "medium-blocks", order: 2 },
  "Pragmatic.Attachments": { slug: "attachments", category: "medium-blocks", order: 3 },
  "Pragmatic.Notes": { slug: "notes", category: "medium-blocks", order: 4 },

  // Platform (preview subsystems)
  "Pragmatic.Agent": { slug: "agent", category: "platform", order: 1 },
  "Pragmatic.Gateway": { slug: "gateway", category: "platform", order: 2 },

  // Source Generator
  // The sync loop skips category "sg" (see sync-docs.mjs), so this entry maps links only — it does
  // not publish anything. The three pages under site/docs/.../source-generator/ are hand-written and
  // unmanaged: their names match none of the six documents in Pragmatic.SourceGenerator/docs/, so
  // nothing regenerates them and nothing checks them.
  "Pragmatic.SourceGenerator": { slug: "../source-generator", category: "sg", order: 1 },
};
