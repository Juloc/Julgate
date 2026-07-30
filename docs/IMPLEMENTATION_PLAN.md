# Julgate implementation plan

Julgate keeps the existing .NET 10, Minimal API and JSON-file architecture. This plan does not introduce PostgreSQL, EF Core, ASP.NET Core Identity or Razor Pages.

## A. Foundation and branding

- Rename product-facing Matgate references to Julgate.
- Keep compatibility aliases for existing environment variables during migration.
- Document current capabilities, data paths and backup requirements.
- Preserve RDP, VNC, SSH, file access, workspaces and PWA behavior.

## B. Critical security

- Reject default administrator credentials and fixed Guacamole secrets.
- Protect saved connection and bridge credentials at rest.
- Use secure, short-lived authentication cookies.
- Add login and request rate limits.
- Enforce request-size and timeout limits.
- Add origin checks and browser security headers.
- Verify target certificates by default.
- Store JSON data and Data Protection keys with private filesystem permissions.
- Record security-relevant administration and connection events.

## C. Gateway hardening

- Keep Guacamole and guacd on an internal Docker network.
- Publish only the edge service.
- Make the website proxy and network tools opt-in.
- Add explicit destination restrictions and SSRF protection to the website proxy.
- Protect file operations against traversal, symlink escape and oversized archives.
- Apply protocol-specific timeouts and limits.

## D. AE01 interface

- Apply a Fluent 2 / Windows 11 visual system.
- Use neutral surfaces, clear borders, compact spacing and visible controls.
- Keep connection tabs, server navigation and status information dense but readable.
- Use consistent design tokens for light, dark and system themes.
- Improve desktop, tablet, mobile and installed-PWA layouts.
- Replace remaining Matgate product text and storage keys with Julgate equivalents.

## E. Tests and CI

- Add unit tests for authentication, password hashing and credential protection.
- Add authorization-matrix tests for administrative and user routes.
- Add regression tests for origin checks, SSRF, path traversal and upload limits.
- Add Playwright smoke tests for login, administration and session launch.
- Build the application and container on every pull request.
- Add CodeQL, dependency review, container scanning and an SBOM.

## F. Deployment and release

- Publish `ghcr.io/juloc/julgate` with immutable version and commit tags.
- Run Julgate as a non-root user with a read-only root filesystem.
- Drop Linux capabilities and enable `no-new-privileges`.
- Provide local, reverse-proxy and Dockhand Compose variants.
- Document backup, restore, key rotation and recovery.
- Validate a staging deployment before the first stable Julgate release.
