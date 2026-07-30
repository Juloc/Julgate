# Julgate

Julgate is a self-hosted home-network gateway for browser-based RDP, VNC, SSH and file access. It uses Apache Guacamole and `guacd` for remote sessions while Julgate provides authentication, permissions, server management, workspaces, tabs and the PWA interface.

Julgate is a security-focused fork of Matgate. The application keeps the existing .NET 10, Minimal API and JSON-file architecture.

## Current release

```text
0.6.1
ghcr.io/juloc/julgate:0.6.1
```

Production Compose files use the fixed version tag by default. Override it with `JULGATE_VERSION` only when intentionally upgrading.

## Features

- RDP, VNC and SSH in the browser
- SFTP, FTP and SMB file access
- global and user-owned connections
- per-server permissions and administrator roles
- multiple draggable connection tabs
- workspaces with files and shared text
- installable PWA
- German and English interface
- AE01-style Fluent interface
- Docker and Dockhand deployment

The website proxy, network tools and archive extraction are high-risk features and are disabled by default.

## Architecture

```text
Browser -> HTTPS reverse proxy -> Julgate edge
                                -> Julgate application
                                -> Apache Guacamole -> guacd -> RDP/VNC/SSH target
                                -> file gateway -> SFTP/FTP/SMB target
```

Only the edge service is published. Julgate, Guacamole and `guacd` remain unreachable from the host network. Julgate and `guacd` receive outbound network access only because they must contact configured targets.

## Security defaults

- no default administrator password
- no fixed Guacamole signing key
- AES-GCM encryption for saved target credentials
- separate credential-encryption key
- PBKDF2-SHA256 password hashing
- secure and short-lived authentication cookies
- login and global request rate limits
- request-size and timeout limits
- origin checks and browser security headers
- remote certificate validation enabled by default
- website proxy disabled by default
- network tools disabled by default and administrator-only when enabled
- archive extraction disabled by default
- non-root Julgate container
- read-only root filesystem
- dropped Linux capabilities and `no-new-privileges`
- separate frontend, backend and egress Docker networks

Read [SECURITY.md](SECURITY.md) before deployment.

## Required configuration

Copy the example file:

```bash
cp .env.example .env
```

Generate secrets:

```bash
openssl rand -hex 16
openssl rand -base64 32
```

Set at least:

```env
JULGATE_VERSION=0.6.1
JULGATE_ADMIN_USER=admin
JULGATE_ADMIN_PASSWORD=use-a-random-password-with-at-least-16-characters
JULGATE_GUACAMOLE_JSON_SECRET_KEY=32-random-hex-characters
JULGATE_CREDENTIAL_KEY=base64-encoded-32-byte-key
```

`JULGATE_CREDENTIAL_KEY` must be stored separately from the `/data` backup.

## Published image deployment

```bash
docker compose -f docker-compose-simple.yaml pull
docker compose -f docker-compose-simple.yaml up -d
```

The default image is:

```text
ghcr.io/juloc/julgate:0.6.1
```

The workflow publishes these tags from `main`:

- `0.6.1`
- `latest`
- `sha-<commit>`

Use the fixed version or SHA tag for production.

## Docker secrets overlay

Create the local secret files:

```bash
mkdir -p secrets
printf '%s' 'your-admin-password' > secrets/julgate_admin_password
openssl rand -base64 32 > secrets/julgate_credential_key
chmod 600 secrets/*
```

Start with the overlay:

```bash
docker compose \
  -f docker-compose-simple.yaml \
  -f docker-compose-secrets.yaml \
  up -d
```

`JULGATE_GUACAMOLE_JSON_SECRET_KEY` remains an environment value because the upstream Guacamole image does not support Docker `*_FILE` variables.

## Local build

The default stack builds the current checkout and tags the local image with `JULGATE_VERSION`:

```bash
docker compose up --build -d
```

Open:

```text
http://127.0.0.1:8088
```

For local HTTP testing:

```env
JULGATE_REQUIRE_SECURE_COOKIES=false
```

For access through an HTTPS reverse proxy, set it to `true`.

## Dockhand

Use `docker-compose-dockhand.yaml`. Configure the required environment values in Dockhand before deployment. The default host bind is `127.0.0.1:8088`, intended for an existing Caddy or another HTTPS reverse proxy.

## Optional features

```env
JULGATE_ENABLE_WEBSITE_PROXY=false
JULGATE_ENABLE_NETWORK_TOOLS=false
JULGATE_ENABLE_ARCHIVE_EXTRACTION=false
```

Do not enable these features unless required. The website proxy can reach internal web interfaces, network tools actively contact configured destinations, and archive extraction can consume significant disk and CPU resources.

## Persistent data

The `/data` volume contains:

- `users.json`
- `servers.json`
- `workspaces.json`
- encrypted stored credentials
- ASP.NET Data Protection keys
- workspace files
- security audit logs

The JSON files use atomic writes and private Unix permissions. Their `.bak` files contain the encrypted current state.

## Existing Matgate data

Before migration, create an offline backup. Copy the existing JSON and workspace data into the Julgate `/data` volume before first start.

On first start, Julgate:

1. reads the current JSON files;
2. encrypts saved server and Guacamole bridge credentials;
3. rewrites the files with private permissions;
4. creates encrypted backup files;
5. removes legacy plaintext Guacamole mapping files.

Keep the configured `JULGATE_CREDENTIAL_KEY`; changing or losing it prevents decryption of stored credentials.

## Development

```bash
dotnet restore Matgate.slnx
dotnet build Matgate.slnx -c Release
dotnet test Matgate.slnx -c Release
```

The internal project and namespace names still use `Matgate` temporarily to avoid a risky all-at-once code rename. Product-facing branding is Julgate.

The implementation roadmap is in [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md).

## License and attribution

Julgate remains licensed under the MIT License. It is based on Matgate by Real-TTX; the original copyright and license terms remain in the repository.
