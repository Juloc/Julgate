# Julgate

Julgate is a self-hosted .NET 10 gateway for browser-based RDP, VNC, SSH, SFTP, FTP and SMB access. Apache Guacamole and `guacd` handle remote sessions while Julgate provides authentication, authorization, server management, workspaces, tabs and the PWA interface.

Julgate keeps the existing Minimal API and JSON-file architecture. It does not require PostgreSQL, EF Core, ASP.NET Core Identity or Razor Pages.

## Current release

```text
0.7.8
ghcr.io/juloc/julgate:0.7.8
```

Production Compose files use the immutable version tag by default. A Git tag such as `v0.7.8` publishes `0.7.8`, the compatible `0.7` tag, `latest` and `sha-<commit>`. The `main` branch publishes only `edge` and `sha-<commit>`, so stable package versions are never overwritten.

## Capabilities

- RDP, VNC and SSH through short-lived encrypted Guacamole launch data
- SFTP, FTP and SMB upload, listing, download, range access and deletion
- global and user-owned connections with explicit access rules
- administrator, server-manager and per-server authorization
- workspaces with shared files, text and expiring public access
- draggable connection tabs and session restore
- installable PWA with German and English UI
- AE01-style Fluent interface for desktop, tablet and phone
- Docker, reverse-proxy and Dockhand deployment

The website proxy, network tools and archive extraction are disabled by default.

## Architecture

```text
Browser -> HTTPS reverse proxy -> Julgate edge
                                -> Julgate application
                                -> Apache Guacamole -> guacd -> RDP/VNC/SSH
                                -> hardened file gateway -> SFTP/FTP/SMB
```

Only the edge service is published. Julgate, Guacamole and `guacd` remain inside the Docker backend network. Julgate and `guacd` receive outbound access because they must contact configured targets.

## Security defaults

- no default administrator password or fixed signing key
- AES-GCM encryption for stored target credentials
- separated primary credential key with controlled previous-key rotation
- PBKDF2-SHA256 user-password hashing
- secure, short-lived, SameSite cookies
- login and global rate limits
- Content-Length and streaming request-size enforcement
- origin, traversal, CSRF and browser-header protection
- target-certificate verification enabled by default
- literal-IP-only optional website proxy to eliminate DNS rebinding
- SFTP/FTP symbolic-link and SMB reparse-point rejection
- file operation, upload, download and directory-entry limits
- bounded archive entries, expanded bytes, concurrency and tmpfs
- administrator-only network tools when enabled
- non-root container, read-only root filesystem and dropped capabilities
- separated frontend, backend and egress networks
- private JSON, backup, key and audit-log permissions

Read [SECURITY.md](SECURITY.md) and [docs/OPERATIONS.md](docs/OPERATIONS.md) before deployment.

## Required configuration

```bash
cp .env.example .env
openssl rand -hex 16
openssl rand -base64 32
```

Set at least:

```env
JULGATE_VERSION=0.7.0
JULGATE_ADMIN_USER=admin
JULGATE_ADMIN_PASSWORD=use-a-random-password-with-at-least-16-characters
JULGATE_GUACAMOLE_JSON_SECRET_KEY=32-random-hex-characters
JULGATE_CREDENTIAL_KEY=base64-encoded-32-byte-key
```

Store `JULGATE_CREDENTIAL_KEY` separately from the `/data` backup.

## Published image deployment

```bash
docker compose -f docker-compose-simple.yaml pull
docker compose -f docker-compose-simple.yaml up -d
```

The default edge bind is `127.0.0.1:8088`. Put an HTTPS reverse proxy in front of it. Set `JULGATE_BIND_ADDRESS=0.0.0.0` only when the firewall and network design explicitly require it.

## Docker secrets

```bash
mkdir -p secrets
printf '%s' 'your-admin-password' > secrets/julgate_admin_password
openssl rand -base64 32 > secrets/julgate_credential_key
chmod 600 secrets/*

docker compose \
  -f docker-compose-simple.yaml \
  -f docker-compose-secrets.yaml \
  up -d
```

The upstream Guacamole image requires its JSON signing key as an environment value. Julgate administrator and credential-encryption secrets support Docker secret files.

During credential-key rotation, add `docker-compose-key-rotation.yaml` for one verified startup with the old key. Follow [docs/OPERATIONS.md](docs/OPERATIONS.md); remove the rotation overlay after the restart succeeds without the previous key.

## Local development

```bash
dotnet restore Matgate.slnx
dotnet build Matgate.slnx -c Release
dotnet test Matgate.slnx -c Release
docker compose up --build -d
```

For local HTTP testing only:

```env
JULGATE_REQUIRE_SECURE_COOKIES=false
```

## File and archive limits

```env
JULGATE_FILE_OPERATION_TIMEOUT_SECONDS=120
JULGATE_MAX_UPLOAD_BYTES=536870912
JULGATE_MAX_DOWNLOAD_BYTES=2147483648
JULGATE_MAX_DIRECTORY_ENTRIES=10000
JULGATE_ENABLE_ARCHIVE_EXTRACTION=false
JULGATE_MAX_ARCHIVE_EXPANDED_BYTES=268435456
JULGATE_MAX_ARCHIVE_ENTRIES=4096
JULGATE_MAX_CONCURRENT_ARCHIVE_EXTRACTIONS=1
```

File paths are validated again at the service boundary. Traversal, SFTP/FTP symbolic links, SMB reparse points and local symbolic-link upload sources are rejected.

## Website proxy

The website proxy is opt-in:

```env
JULGATE_ENABLE_WEBSITE_PROXY=false
```

When enabled, targets must use explicit permitted IP addresses. DNS hostnames, loopback, link-local, multicast and known cloud metadata targets are rejected.

## Persistent data and migration

The `/data` volume contains users, servers, workspaces, encrypted credentials, ASP.NET Data Protection keys, workspace files and the security audit log.

On first start with existing Matgate data, Julgate:

1. reads the existing JSON files;
2. encrypts stored server and Guacamole bridge credentials;
3. rewrites current and backup files with private permissions;
4. removes legacy plaintext Guacamole mapping files;
5. preserves the existing JSON architecture and user data.

Create an offline backup first. Losing the matching credential key makes stored connection passwords unrecoverable.

## Automated validation

Pull requests and `main` execute:

- .NET restore, build and unit tests
- authorization-matrix and RDP/VNC/SSH launch tests
- full hardened Compose startup and HTTP security smoke tests
- legacy plaintext-data migration in a real container
- Playwright login, administration, primary-page, PWA and responsive tests
- real SFTP, FTP and SMB roundtrips against disposable protocol servers
- CodeQL and direct/transitive NuGet audit
- Trivy image and configuration scans
- SBOM and build provenance
- backup creation, checksum verification and disposable-volume restore drill

## Internal compatibility names

The internal project, assembly and namespace names remain `Matgate` as a deliberate binary and source compatibility boundary. Product-facing text, PWA metadata, cookies and browser storage use Julgate. This internal name is not a remaining product-branding task.

## License and attribution

Julgate remains licensed under the MIT License. It is based on Matgate by Real-TTX; the original copyright and license terms remain in the repository.
