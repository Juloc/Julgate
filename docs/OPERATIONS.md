# Julgate operations runbook

This runbook is the required operating procedure for Julgate deployments.

## Release and image policy

Production deployments must use an immutable version tag:

```text
ghcr.io/juloc/julgate:0.7.0
```

Do not use `latest` as the only production reference. Keep the previous known-good version in the deployment history for rollback.

## Required secrets

- `JULGATE_ADMIN_PASSWORD`: initial administrator password; at least 16 characters.
- `JULGATE_GUACAMOLE_JSON_SECRET_KEY`: exactly 32 hexadecimal characters.
- `JULGATE_CREDENTIAL_KEY`: Base64-encoded 32-byte AES-GCM key.
- `JULGATE_CREDENTIAL_KEY_PREVIOUS`: optional comma-separated previous keys used only during rotation.

Generate keys with:

```bash
openssl rand -hex 16
openssl rand -base64 32
```

The credential key must be backed up separately from the Docker data volume. A data backup without the matching key cannot restore stored connection credentials.

## Persistent resources

- `julgate-data`: users, servers, workspaces, audit log and ASP.NET Data Protection keys.
- `julgate-drives`: Guacamole redirected-drive content.
- credential key: stored outside both volumes.
- deployment `.env` or Docker secrets: stored separately from the application backup.

## Backup

Run:

```bash
bash scripts/backup.sh
```

The script:

1. creates timestamped archives for `julgate-data` and `julgate-drives`;
2. records SHA-256 checksums;
3. writes a manifest containing the application version and volume names;
4. never copies the credential key into the same archive automatically.

Copy the backup directory and the credential-key backup to separate protected locations.

## Restore drill

A restore is not considered valid until it has been tested.

1. Stop Julgate.
2. Preserve the current volumes under different names or create a fresh backup.
3. Restore the selected archives with `scripts/restore.sh`.
4. Restore the matching `JULGATE_CREDENTIAL_KEY`.
5. Start the exact Julgate version recorded in the backup manifest.
6. Verify `/healthz`.
7. Sign in with an administrator account.
8. Open at least one stored connection and verify that its credential decrypts.
9. Verify users, workspaces and audit history.
10. Run `scripts/verify-deployment.sh`.

Example:

```bash
bash scripts/restore.sh backups/julgate-2026-07-30_120000
```

The restore script refuses to overwrite non-empty volumes unless `JULGATE_RESTORE_FORCE=true` is set.

## Credential-key rotation

Julgate supports online re-encryption during startup.

1. Create and verify a full backup.
2. Keep the current key as `JULGATE_CREDENTIAL_KEY_PREVIOUS`.
3. Generate a new key and set it as `JULGATE_CREDENTIAL_KEY`.
4. Start Julgate once. During startup, stored values are decrypted using the previous key and written using the new primary key.
5. Verify login and stored RDP, VNC, SSH, SFTP, FTP and SMB credentials.
6. Remove `JULGATE_CREDENTIAL_KEY_PREVIOUS`.
7. Restart Julgate and run the deployment verification again.
8. Retain the old key only with backups created before the rotation, according to the backup-retention policy.

The helper script performs the environment-file update and validation sequence:

```bash
bash scripts/rotate-credential-key.sh /path/to/julgate/.env
```

## Rollback

A rollback must restore both code and compatible data:

1. stop the current stack;
2. select the previous immutable image tag;
3. if the new version changed or re-encrypted persistent data, restore the matching pre-upgrade volume backup and key;
4. start the previous version;
5. run the deployment verification;
6. document the failed version and preserve logs.

Do not point an old version at data that was already migrated by a newer version unless that downgrade path was explicitly tested.

## Staging acceptance checklist

Every release must pass the automated ephemeral staging stack and the following checks on the target network:

- login and logout;
- administrator and non-administrator authorization;
- desktop, tablet and phone layouts;
- PWA manifest and service-worker registration;
- RDP launch and redirected drive;
- VNC launch;
- SSH launch and terminal resize;
- SFTP upload, list, download and delete;
- FTP upload, list, download and delete when FTP is enabled;
- SMB upload, list, download and delete;
- workspace public-link expiry and upload permissions;
- website proxy only with an explicit IP target when enabled;
- backup creation and restore into disposable volumes;
- credential-key rotation and restart without the previous key.

## File-gateway limits

Recommended production defaults:

```env
JULGATE_FILE_OPERATION_TIMEOUT_SECONDS=120
JULGATE_MAX_UPLOAD_BYTES=536870912
JULGATE_MAX_DOWNLOAD_BYTES=2147483648
JULGATE_MAX_DIRECTORY_ENTRIES=10000
JULGATE_MAX_ARCHIVE_EXPANDED_BYTES=268435456
JULGATE_MAX_ARCHIVE_ENTRIES=4096
JULGATE_MAX_CONCURRENT_ARCHIVE_EXTRACTIONS=1
```

File gateway paths reject traversal, SFTP/FTP symbolic links and SMB reparse points. Archive extraction is disabled by default and additionally constrained by byte, entry, concurrency and container tmpfs limits when enabled.

## Website proxy

The optional website proxy is disabled by default. To eliminate DNS rebinding, targets must use literal permitted IP addresses. Loopback, link-local, multicast and known cloud metadata addresses are blocked.

## Incident collection

Preserve:

- the immutable image tag and digest;
- container logs;
- `/data/audit/security.jsonl`;
- relevant reverse-proxy logs;
- the failing request trace ID;
- a copy of the deployment configuration with all secrets removed.

Never attach raw credentials, cookies, Guacamole launch data or decrypted JSON files to a public issue.
