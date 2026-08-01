# Changelog

## 0.7.7 — 2026-08-01

### Remote sessions

- Replace the custom Android `beforeinput` forwarding path with Guacamole 1.6 `InputSink`.
- Route native mobile and hardware keyboard input through one `Guacamole.Keyboard` instance so RDP characters are sent exactly once.
- Add a regression test that rejects the former duplicate mobile input path.

### Upstream

- Recheck `Real-TTX/Matgate` at `8f27b00585f44e68e6867a1b5a21eb08cc32f441`; no newer upstream commits require integration.
- Keep selective upstream synchronization instead of merging security, credential or deployment code over Julgate.

## 0.7.6 — 2026-07-31

### Interface

- Keep the SMB/SFTP/FTP file viewer close action inside its dialog instead of allowing the event to reach the connection-tab or browser navigation handlers.
- Add a Playwright regression test proving that closing an embedded file viewer leaves the Julgate page and active connection tab open.

### Reliability

- Separate login, state-changing and read request limits so normal interactive tabs cannot exhaust a single IP-wide request bucket.
- Partition authenticated traffic per user and include a `Retry-After` response for genuine throttling.

### Upstream

- Review the four commits added to `Real-TTX/Matgate` after the Julgate fork point.
- Keep Julgate security, credential and deployment implementations instead of replacing them with overlapping upstream changes.
- Track missing upstream UI features for selective porting rather than blind full-tree merges.

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
