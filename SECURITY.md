# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in Pragmatic.Design, please report it responsibly.

**Do NOT open a public issue for security vulnerabilities.**

Instead, report it privately through GitHub — **Security → Report a vulnerability** on this repository —
or email **security@pragmaticdesign.net**.

Include:
- Description of the vulnerability
- Steps to reproduce
- Affected module(s) and version(s)
- Potential impact

## Response Timeline

- **Acknowledgment**: Within 48 hours
- **Assessment**: Within 1 week
- **Fix**: Depends on severity, typically within 2 weeks for critical issues

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest  | Yes       |

Only the latest published version receives security fixes.

## Scope

This policy covers every package in the Pragmatic.Design ecosystem, under either licence: the parts
released under MIT and the parts released under PolyForm Small Business 1.0.0.

**In scope**: vulnerabilities in the framework's own code — anything that lets a consumer's application
be compromised by using the framework as documented. Examples: an authorization check that can be
bypassed, tenant isolation that leaks across boundaries, a value that fails to be encrypted or redacted
where the API says it is.

**Not in scope**: vulnerabilities in an application built *with* the framework but caused by its own
code or configuration; issues in third-party dependencies (report those upstream — though tell us if we
ship an affected version); and the sample and showcase projects, which exist to demonstrate features and
are not hardened for production.

## Dependencies

The build gate runs `dotnet list package --vulnerable` on every full run, so a dependency with a known
advisory fails the build before it can reach a release. It also writes a CycloneDX SBOM per package
under `artifacts/sbom/`.

## Disclosure

- **Advisories** are published as GitHub Security Advisories on this repository, once a fixed version
  is available. A CVE identifier is requested through GitHub for any vulnerability that affects a
  published package.
- **The disclosure date** is agreed with the reporter; the default is the release of the fix.
- **Reporters are credited** in the advisory, unless they ask not to be.
- **Fixes ship in the next version** only. While the project is in preview there are no backports to
  earlier versions.
- **To follow security updates**, watch this repository for security alerts (Watch → Custom → Security
  alerts); a published advisory also reaches anyone whose dependencies GitHub scans.
