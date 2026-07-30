# Julgate implementation status

Julgate keeps the existing .NET 10, Minimal API and JSON-file architecture. PostgreSQL, EF Core, ASP.NET Core Identity and Razor Pages are not part of this implementation.

Release target: **0.7.0**.

## A. Foundation and branding — complete

- Product-facing text, PWA metadata and browser surfaces use Julgate.
- Legacy environment-variable aliases remain available for migration compatibility.
- Legacy preference cookies and browser storage are migrated to Julgate names.
- RDP, VNC, SSH, file access, workspaces and PWA behavior remain supported.
- Internal project, assembly and namespace names remain `Matgate` as a deliberate compatibility boundary. They are not exposed product branding and are not scheduled for a risky all-at-once rename.

## B. Critical security — complete

- Default administrator passwords and fixed Guacamole keys are rejected.
- Stored target and bridge credentials use AES-GCM.
- Primary and previous credential keys support controlled rotation.
- Authentication cookies are short-lived, secure-configurable, HttpOnly and SameSite.
- Login and global request rate limits are active.
- Content-Length and streaming request bodies are bounded.
- Origin, traversal, CSRF and browser-header guards are active.
- Target certificates are verified by default.
- JSON, backup, key and audit files use private permissions.
- Authenticated administration and session access are audited without request bodies or credentials.

## C. Gateway hardening — complete

- Guacamole and `guacd` remain on the internal backend network.
- Only the edge service is published.
- Website proxy, network tools and archive extraction are opt-in.
- Website proxy targets require literal permitted IP addresses; DNS rebinding is eliminated.
- File paths are validated at both HTTP and service boundaries.
- SFTP/FTP symbolic links, SMB reparse points and local symbolic-link uploads are rejected.
- Upload, download, directory-entry and operation-time limits are enforced.
- Archive expanded bytes, entries, concurrency and tmpfs are bounded.
- Network tools require an administrator account when enabled.

## D. AE01 interface — complete

- Fluent/Windows-style design tokens, neutral surfaces, visible borders and compact controls are applied.
- Light, dark, system and reduced-motion modes are supported.
- Desktop, tablet, phone and installed-PWA layouts are covered by Playwright.
- Primary administration, account, workspace, session and about pages are checked for horizontal overflow and HTTP errors.
- Product text, manifest metadata, preference cookies and browser storage use Julgate names.

## E. Tests and CI — complete

- Credential protection, hashing, key rotation and migration tests.
- Full server-access and editing authorization matrix.
- RDP, VNC and SSH encrypted-launch tests.
- Origin, SSRF, traversal, upload, request and archive-limit regression tests.
- Playwright login, protected-route, branding, storage-migration and responsive-page tests.
- Real SFTP, FTP and SMB upload/list/download/delete roundtrips.
- Hardened Compose startup and HTTP security smoke tests.
- Real legacy plaintext JSON migration in a container.
- CodeQL and direct/transitive NuGet vulnerability audit.
- Trivy image and configuration scans.
- SBOM and provenance generation.
- Real Docker-volume backup and restore drill.

## F. Deployment and release — complete

- `ghcr.io/juloc/julgate` publishes immutable version and commit tags.
- Julgate runs non-root with a read-only root filesystem.
- Linux capabilities are dropped and `no-new-privileges` is enabled.
- Local, published-image and Dockhand Compose variants are aligned.
- Docker secret and previous-key rotation overlays are provided.
- Backup, restore, rollback, key rotation, staging acceptance and incident collection are documented.
- Ephemeral staging, protocol integration and recovery validation run automatically before merge.

## Release gate

Version 0.7.0 may be merged only when every current PR workflow is successful:

- Julgate build
- Julgate security
- Julgate completion validation
- Julgate file protocol integration
- Julgate operations validation

After merge, the main image must be published and pulled successfully by the deployment-repository smoke test before the Docker stack is merged.
