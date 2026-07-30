# Changelog

## 0.7.0 — 2026-07-30

### Security

- Enforce file-path safety at the service boundary.
- Reject SFTP/FTP symbolic links, SMB reparse points and local symbolic-link upload sources.
- Add upload, download, directory-entry, request-body and protocol-operation limits.
- Bound archive expanded size, entry count and concurrent extraction.
- Eliminate website-proxy DNS rebinding by requiring literal permitted IP targets.
- Add controlled AES-GCM credential-key rotation using a temporary previous key.
- Complete Julgate preference-cookie and browser-storage migration.

### Interface

- Complete product-facing Julgate branding, including PWA manifest output.
- Verify primary views at desktop, tablet and phone sizes through Playwright.

### Testing

- Add authorization-matrix and RDP/VNC/SSH launch-token tests.
- Add real SFTP, FTP and SMB roundtrip tests.
- Add real legacy-data migration, Trivy and Playwright staging validation.
- Add Docker-volume backup and restore drills.

### Operations

- Add immutable 0.7.0 deployment stacks with resource limits.
- Add Docker secret and previous-key rotation overlays.
- Add backup, restore, verification, rollback and rotation runbooks and scripts.

## 0.6.1 — 2026-07-30

- Initial Julgate security hardening and AE01 interface release.
- Added encrypted stored credentials, hardened containers, CodeQL, NuGet auditing, SBOM and Compose smoke tests.
