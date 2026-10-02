import { defineConfig } from "astro/config";
import starlight from "@astrojs/starlight";

export default defineConfig({
  integrations: [
    starlight({
      title: "Pragmatic Design",
      description:
        "The Foundry of Precision Code — A .NET meta-framework powered by Source Generators.",
      logo: {
        src: "./src/assets/logo.png",
        replacesTitle: false,
      },
      social: [
        { label: "GitHub", icon: "github", href: "https://github.com/pragmatic-design/Pragmatic.Design" },
        { label: "Website", icon: "external", href: "https://pragmaticdesign.net" },
      ],
      editLink: {
        baseUrl: "https://github.com/pragmatic-design/Pragmatic.Design/edit/main/site/docs/",
      },
      expressiveCode: {
        themes: ["github-dark"],
        styleOverrides: {
          borderColor: "#2a2a2a",
          borderRadius: "0.5rem",
          codeBackground: "#181818",
          codePaddingBlock: "1rem",
          codePaddingInline: "1.25rem",
          frames: {
            terminalTitlebarDotsForeground: "rgba(255,255,255,0.6)",
            terminalTitlebarDotsOpacity: "1",
            terminalTitlebarBackground: "#1a1a1a",
            terminalBackground: "#181818",
          },
        },
      },
      favicon: "/favicon.ico",
      head: [
        {
          tag: "link",
          attrs: {
            rel: "icon",
            type: "image/png",
            href: "/favicon-96x96.png",
            sizes: "96x96",
          },
        },
        {
          tag: "link",
          attrs: {
            rel: "apple-touch-icon",
            sizes: "180x180",
            href: "/apple-touch-icon.png",
          },
        },
        {
          tag: "link",
          attrs: {
            rel: "manifest",
            href: "/site.webmanifest",
          },
        },
        {
          tag: "script",
          content: `
            document.addEventListener('DOMContentLoaded', () => {
              const footer = document.createElement('div');
              footer.style.cssText = 'position:fixed;bottom:0;left:0;right:0;z-index:100;background:#1a1a1a;border-top:1px solid #4a4640;font-family:Inter,system-ui,sans-serif;';
              footer.innerHTML = '<div style="display:flex;align-items:center;justify-content:space-between;padding:8px 24px;max-width:1400px;margin:0 auto;">'
                + '<span style="color:#ffe088;font-size:13px;">⚠️ <strong>v1.0.0-alpha</strong> — APIs may change before the 1.0 release</span>'
                + '<span style="display:flex;align-items:center;gap:16px;font-size:12px;">'
                + '<a href="https://pragmaticdesign.net" style="color:#d0c5af;text-decoration:none;" onmouseover="this.style.color=\\'#ffe088\\'" onmouseout="this.style.color=\\'#d0c5af\\'">← pragmaticdesign.net</a>'
                + '<span style="color:#4a4640;">|</span>'
                + '<span style="color:#99907c;">© 2026 Alessandro Saiani</span>'
                + '</span></div>';
              document.body.appendChild(footer);
            });
          `,
        },
      ],
      components: {
        SiteTitle: "./src/components/SiteTitle.astro",
      },
      customCss: ["./src/styles/custom.css"],
      sidebar: [
        {
          label: "Getting Started",
          items: [
            { label: "Introduction", slug: "getting-started/introduction" },
            { label: "Installation", slug: "getting-started/installation" },
            { label: "Build with an agent", slug: "getting-started/with-an-agent" },
            { label: "Architecture", slug: "getting-started/architecture" },
          ],
        },
        {
          label: "Guides",
          autogenerate: { directory: "guides" },
        },
        {
          label: "Foundation",
          items: [
            { label: "Result", collapsed: true, autogenerate: { directory: "modules/result" } },
            { label: "Ensure", collapsed: true, autogenerate: { directory: "modules/ensure" } },
            { label: "Abstractions", collapsed: true, autogenerate: { directory: "modules/abstractions" } },
          ],
        },
        {
          label: "Core Modules",
          items: [
            { label: "Endpoints", collapsed: true, autogenerate: { directory: "modules/endpoints" } },
            { label: "Actions", collapsed: true, autogenerate: { directory: "modules/actions" } },
            { label: "Persistence", collapsed: true, autogenerate: { directory: "modules/persistence" } },
            { label: "Composition", collapsed: true, autogenerate: { directory: "modules/composition" } },
            { label: "Events", collapsed: true, autogenerate: { directory: "modules/events" } },
            { label: "Messaging", collapsed: true, autogenerate: { directory: "modules/messaging" } },
            { label: "Client", collapsed: true, autogenerate: { directory: "modules/client" } },
          ],
        },
        {
          label: "Capabilities",
          collapsed: true,
          items: [
            { label: "Validation", collapsed: true, autogenerate: { directory: "modules/validation" } },
            { label: "Mapping", collapsed: true, autogenerate: { directory: "modules/mapping" } },
            { label: "Caching", collapsed: true, autogenerate: { directory: "modules/caching" } },
            { label: "Specification", collapsed: true, autogenerate: { directory: "modules/specification" } },
            { label: "Patch", collapsed: true, autogenerate: { directory: "modules/patch" } },
            { label: "Configuration", collapsed: true, autogenerate: { directory: "modules/configuration-module" } },
            { label: "Testing", collapsed: true, autogenerate: { directory: "modules/testing" } },
          ],
        },
        {
          label: "Infrastructure",
          collapsed: true,
          items: [
            { label: "Identity & Auth", collapsed: true, autogenerate: { directory: "modules/identity" } },
            { label: "Authorization", collapsed: true, autogenerate: { directory: "modules/authorization" } },
            { label: "Resilience", collapsed: true, autogenerate: { directory: "modules/resilience" } },
            { label: "Internationalization", collapsed: true, autogenerate: { directory: "modules/i18n" } },
            { label: "Temporal", collapsed: true, autogenerate: { directory: "modules/temporal" } },
            { label: "Multi-Tenancy", collapsed: true, autogenerate: { directory: "modules/multi-tenancy" } },
            { label: "Storage", collapsed: true, autogenerate: { directory: "modules/storage" } },
            { label: "Logging", collapsed: true, autogenerate: { directory: "modules/logging" } },
            { label: "Feature Flags", collapsed: true, autogenerate: { directory: "modules/feature-flags" } },
            { label: "Discovery", collapsed: true, autogenerate: { directory: "modules/discovery" } },
            { label: "Jobs", collapsed: true, autogenerate: { directory: "modules/jobs" } },
            { label: "Migrations", collapsed: true, autogenerate: { directory: "modules/migrations" } },
          ],
        },
        {
          label: "Compliance",
          collapsed: true,
          items: [
            { label: "Privacy", collapsed: true, autogenerate: { directory: "modules/privacy" } },
            { label: "Audit", collapsed: true, autogenerate: { directory: "modules/audit" } },
            { label: "Redaction", collapsed: true, autogenerate: { directory: "modules/redaction" } },
            { label: "Cryptography", collapsed: true, autogenerate: { directory: "modules/cryptography" } },
            { label: "Incidents", collapsed: true, autogenerate: { directory: "modules/incidents" } },
          ],
        },
        {
          label: "Documents & Media",
          collapsed: true,
          items: [
            { label: "Documents", collapsed: true, autogenerate: { directory: "modules/documents" } },
            { label: "Imaging", collapsed: true, autogenerate: { directory: "modules/imaging" } },
            { label: "Email", collapsed: true, autogenerate: { directory: "modules/email" } },
            { label: "Notifications", collapsed: true, autogenerate: { directory: "modules/notifications" } },
          ],
        },
        {
          label: "Medium Blocks",
          collapsed: true,
          items: [
            { label: "Comments", collapsed: true, autogenerate: { directory: "modules/comments" } },
            { label: "Tags", collapsed: true, autogenerate: { directory: "modules/tags" } },
            { label: "Attachments", collapsed: true, autogenerate: { directory: "modules/attachments" } },
            { label: "Notes", collapsed: true, autogenerate: { directory: "modules/notes" } },
          ],
        },
        {
          label: "Platform (Preview)",
          collapsed: true,
          items: [
            { label: "Agent", collapsed: true, autogenerate: { directory: "modules/agent" } },
            { label: "Gateway", collapsed: true, autogenerate: { directory: "modules/gateway" } },
          ],
        },
        {
          label: "Source Generator",
          autogenerate: { directory: "source-generator" },
        },
        {
          label: "Reference",
          autogenerate: { directory: "reference" },
        },
      ],
      defaultLocale: "en",
    }),
  ],
  site: "https://docs.pragmaticdesign.net",
});
