# Julgate releases

Julgate GitHub Releases are driven by `Matgate/Matgate.csproj` and `CHANGELOG.md`.

## Create the next release

1. Set `VersionPrefix` to the next stable semantic version, for example `0.7.9`.
2. Add a matching `## 0.7.9 — YYYY-MM-DD` section to `CHANGELOG.md`.
3. Merge the validated change into `main`.

The release workflow starts after the normal `Julgate build` workflow completes. It verifies that the following workflows all succeeded for exactly the same `main` commit:

- Julgate build
- Julgate security
- Julgate completion validation
- Julgate file protocol integration
- Julgate operations validation

Only then does it:

- publish `ghcr.io/juloc/julgate:X.Y.Z`, `:X.Y`, `:latest` and `:sha-...`;
- create GitHub Release `Julgate X.Y.Z` and tag `vX.Y.Z`;
- use the matching changelog section as release notes;
- attach `julgate-X.Y.Z-deployment.tar.gz` and its SHA-256 checksum.

If that release already exists, the workflow exits without replacing it. Ordinary commits with an unchanged `VersionPrefix` continue to publish only the `edge` and SHA package tags through the normal build workflow.

The release fails closed when the version is not stable SemVer, the changelog section is missing, a required validation failed, or an existing tag points to a different commit.
