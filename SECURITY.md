# Julgate security policy

## Supported deployment

Julgate is intended to run behind an HTTPS reverse proxy or inside a trusted VPN. Do not publish the Julgate application, Apache Guacamole or `guacd` containers directly.

The supplied Compose files publish only the edge service and bind it to `127.0.0.1` by default. Set `JULGATE_BIND_ADDRESS=0.0.0.0` only when the host firewall and network design require it.

## Required secrets

The following values must be unique and must not be committed:

- `JULGATE_ADMIN_PASSWORD`: initial administrator password with at least 16 characters.
- `JULGATE_GUACAMOLE_JSON_SECRET_KEY`: exactly 32 random hexadecimal characters.
- `JULGATE_CREDENTIAL_KEY`: Base64-encoded random 32-byte key used for AES-GCM encryption of saved target credentials.

Generate the keys with:

```bash
openssl rand -hex 16
openssl rand -base64 32
```

Store `JULGATE_CREDENTIAL_KEY` separately from the Julgate data backup. Losing it makes stored connection credentials unrecoverable. Copying it into the same unprotected backup removes the benefit of separated encryption.

## Data backup

Back up the complete `/data` volume. It contains users, servers, workspaces, Guacamole configuration and ASP.NET Data Protection keys.

Do not restore `/data` without restoring the matching `JULGATE_CREDENTIAL_KEY`. Before an upgrade, create a tested backup of both items in separate protected locations.

## High-risk features

These features are disabled by default:

- website proxy: `JULGATE_ENABLE_WEBSITE_PROXY=true`
- network tools: `JULGATE_ENABLE_NETWORK_TOOLS=true`

Enable them only for trusted administrators and after reviewing destination restrictions. FTP should be avoided when SFTP or SMB is available.

## Reporting a vulnerability

Do not publish credentials, session data or a working exploit in a public issue. Send the repository owner a private security report through GitHub Security Advisories with the affected version, reproduction steps and impact.
