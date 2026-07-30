# Julgate security policy

## Supported deployment

Julgate is intended to run behind an HTTPS reverse proxy or inside a trusted VPN. Do not publish the Julgate application, Apache Guacamole or `guacd` directly.

The supplied Compose files publish only the edge service and bind it to `127.0.0.1` by default. Production deployments must use an immutable release or SHA tag. Version 0.7.0 uses:

```text
ghcr.io/juloc/julgate:0.7.0
```

## Required secrets

The following values must be unique and must not be committed:

- `JULGATE_ADMIN_PASSWORD`: initial administrator password with at least 16 characters.
- `JULGATE_GUACAMOLE_JSON_SECRET_KEY`: exactly 32 random hexadecimal characters.
- `JULGATE_CREDENTIAL_KEY`: Base64-encoded random 32-byte key for AES-GCM encryption.
- `JULGATE_CREDENTIAL_KEY_PREVIOUS`: previous key only during a controlled rotation.

Generate keys with:

```bash
openssl rand -hex 16
openssl rand -base64 32
```

Store the credential key separately from the data-volume backup. A data copy without the matching key cannot restore stored target credentials.

`docker-compose-secrets.yaml` supports file-backed administrator and primary credential keys. `docker-compose-key-rotation.yaml` adds a file-backed previous key only during rotation. The Guacamole JSON key remains an environment value because the upstream image does not support a `*_FILE` setting.

## Authentication and browser boundaries

Julgate uses HttpOnly SameSite cookies, configurable secure-cookie enforcement, short sessions, rate limiting, origin checks and security headers. State-changing cross-origin browser requests are rejected. Workspace cookies are hardened separately.

Product preference cookies and browser storage are migrated from legacy Matgate names to Julgate names. Internal assembly and namespace names do not define a browser security boundary.

## Stored credentials and rotation

Saved server and Guacamole bridge credentials are encrypted with AES-GCM. During rotation, the new key is primary and the old key is supplied as previous. Startup decrypts existing values and rewrites them with the primary key. The old key must then be removed and a second restart verified.

Follow [docs/OPERATIONS.md](docs/OPERATIONS.md). Never rotate without a tested pre-rotation backup.

## File gateway

File operations enforce:

- repeated-decoding and traversal rejection at the HTTP and service boundaries;
- safe leaf names;
- operation timeouts;
- upload, download and directory-entry limits;
- rejection of local symbolic-link upload sources;
- rejection of SFTP and FTP symbolic links in remote paths;
- rejection of SMB reparse points;
- root-delete protection.

Archive extraction is disabled by default. When enabled, expanded bytes, entry count, concurrent extractions and container tmpfs are bounded. These controls reduce resource-exhaustion and link-escape risk; archives must still be accepted only from trusted users.

FTP sends credentials and data without transport encryption unless protected externally. Prefer SFTP or SMB.

## Website proxy

The optional website proxy is disabled by default. To eliminate DNS-rebinding attacks, configured targets must use explicit permitted IP addresses. DNS names, embedded credentials, loopback, link-local, multicast and known cloud metadata targets are rejected.

## Network tools

Network tools are disabled by default and require an administrator account when enabled. They actively contact network destinations and should remain disabled unless operationally required.

## Data backup and recovery

Back up the `julgate-data` and `julgate-drives` volumes and store the matching credential key separately. A backup is accepted only after checksum verification and a restore drill into disposable volumes.

Use:

```bash
bash scripts/backup.sh
bash scripts/restore.sh <backup-directory>
bash scripts/verify-deployment.sh
```

## Automated security validation

Every pull request executes CodeQL, NuGet auditing, Trivy image/configuration scanning, unit and authorization tests, Playwright tests, real SFTP/FTP/SMB roundtrips, hardened container smoke tests, plaintext-migration verification and a backup/restore drill.

## Reporting a vulnerability

Do not publish credentials, cookies, launch data, decrypted JSON files or a working exploit in a public issue. Use GitHub Security Advisories and include the affected version, reproduction steps and impact.
