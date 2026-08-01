from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read_utf8(path: Path) -> tuple[str, bool]:
    raw = path.read_bytes()
    has_bom = raw.startswith(b"\xef\xbb\xbf")
    return raw.decode("utf-8-sig"), has_bom


def write_utf8(path: Path, content: str, has_bom: bool) -> None:
    encoded = content.encode("utf-8")
    if has_bom:
        encoded = b"\xef\xbb\xbf" + encoded
    path.write_bytes(encoded)


def replace_once(content: str, old: str, new: str, label: str) -> str:
    count = content.count(old)
    if count != 1:
        raise RuntimeError(f"Expected one {label}, found {count}.")
    return content.replace(old, new, 1)


workflow_path = ROOT / ".github" / "workflows" / "docker-image.yml"
workflow, workflow_bom = read_utf8(workflow_path)

workflow = replace_once(
    workflow,
    "permissions:\n  contents: read\n  packages: write\n",
    "permissions:\n  contents: write\n  packages: write\n",
    "workflow contents permission",
)

workflow = replace_once(
    workflow,
    "      - name: Checkout\n        uses: actions/checkout@v6\n",
    "      - name: Checkout\n        uses: actions/checkout@v6\n        with:\n          fetch-depth: 0\n",
    "checkout step",
)

old_main_channel = '''          elif [[ "${GITHUB_REF}" == "refs/heads/main" ]]; then
            version="${base_version}-edge.${GITHUB_RUN_NUMBER}"
            publish=true
            channel=edge
          else
'''
new_main_channel = '''          elif [[ "${GITHUB_REF}" == "refs/heads/main" ]]; then
            git fetch --force --tags
            tag="v${base_version}"
            if git rev-parse --verify --quiet "refs/tags/${tag}" >/dev/null \
              && gh release view "${tag}" >/dev/null 2>&1; then
              version="${base_version}-edge.${GITHUB_RUN_NUMBER}"
              channel=edge
            else
              version="${base_version}"
              channel=release
            fi
            publish=true
          else
'''
workflow = replace_once(
    workflow,
    old_main_channel,
    new_main_channel,
    "main release-channel selection",
)

workflow = replace_once(
    workflow,
    "      - name: Compute version\n        id: version\n        shell: bash\n",
    "      - name: Compute version\n        id: version\n        env:\n          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}\n        shell: bash\n",
    "version step environment",
)

release_step = '''

      - name: Create versioned GitHub Release
        if: steps.version.outputs.channel == 'release' && github.event_name == 'push'
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          VERSION: ${{ steps.version.outputs.base_version }}
          IMAGE_NAME: ${{ steps.image.outputs.name }}
        shell: bash
        run: |
          set -euo pipefail
          tag="v${VERSION}"

          git fetch --force --tags
          if git rev-parse --verify --quiet "refs/tags/${tag}" >/dev/null; then
            tag_commit="$(git rev-list -n 1 "${tag}")"
            if [[ "${tag_commit}" != "${GITHUB_SHA}" ]]; then
              echo "Existing tag ${tag} points to ${tag_commit}, not ${GITHUB_SHA}."
              exit 1
            fi
          else
            git config user.name "github-actions[bot]"
            git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
            git tag --annotate "${tag}" --message "Julgate ${VERSION}"
            git push origin "${tag}"
          fi

          if gh release view "${tag}" >/dev/null 2>&1; then
            echo "GitHub Release ${tag} already exists."
            exit 0
          fi

          awk -v version="${VERSION}" '
            $0 ~ "^## " version "([[:space:]]|$)" { capture=1; next }
            capture && /^## / { exit }
            capture { print }
          ' CHANGELOG.md > /tmp/julgate-changelog.md

          if [[ ! -s /tmp/julgate-changelog.md ]]; then
            echo "CHANGELOG.md has no section for ${VERSION}."
            exit 1
          fi

          {
            echo "## Container image"
            echo
            echo "\`${IMAGE_NAME}:${VERSION}\`"
            echo
            cat /tmp/julgate-changelog.md
          } > /tmp/julgate-release-notes.md

          staging="$(mktemp -d)"
          cp docker-compose-simple.yaml "${staging}/"
          cp docker-compose-dockhand.yaml "${staging}/"
          cp docker-compose-secrets.yaml "${staging}/"
          cp docker-compose-key-rotation.yaml "${staging}/"
          cp .env.example "${staging}/julgate.env.example"
          cp docs/OPERATIONS.md "${staging}/OPERATIONS.md"

          archive="julgate-${VERSION}-deployment.tar.gz"
          tar --create --gzip --file "${archive}" --directory "${staging}" .
          sha256sum "${archive}" > "${archive}.sha256"

          gh release create "${tag}" \
            --verify-tag \
            --title "Julgate ${VERSION}" \
            --notes-file /tmp/julgate-release-notes.md \
            --latest \
            "${archive}" \
            "${archive}.sha256"
'''
workflow = replace_once(
    workflow,
    "          sbom: true",
    "          sbom: true" + release_step,
    "release step insertion point",
)
write_utf8(workflow_path, workflow, workflow_bom)

release_test = ROOT / "Matgate.Tests" / "GitHubReleaseWorkflowTests.cs"
release_test.write_text(
    '''using Xunit;

namespace Matgate.Tests;

public sealed class GitHubReleaseWorkflowTests
{
    [Fact]
    public void BuildWorkflow_CreatesVersionedGitHubReleaseFromMain()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "docker-image.yml"));

        Assert.Contains("contents: write", workflow, StringComparison.Ordinal);
        Assert.Contains("fetch-depth: 0", workflow, StringComparison.Ordinal);
        Assert.Contains("gh release view", workflow, StringComparison.Ordinal);
        Assert.Contains("Create versioned GitHub Release", workflow, StringComparison.Ordinal);
        Assert.Contains("git tag --annotate", workflow, StringComparison.Ordinal);
        Assert.Contains("gh release create", workflow, StringComparison.Ordinal);
        Assert.Contains("CHANGELOG.md has no section", workflow, StringComparison.Ordinal);
        Assert.Contains("julgate-${VERSION}-deployment.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("sha256sum", workflow, StringComparison.Ordinal);
    }
}
''',
    encoding="utf-8",
)

docs_path = ROOT / "docs" / "RELEASING.md"
docs_path.write_text(
    '''# Julgate releases

Julgate releases are driven by `Matgate/Matgate.csproj` and `CHANGELOG.md`.

## Create a release

1. Set `VersionPrefix` to the next stable semantic version, for example `0.7.9`.
2. Add a matching `## 0.7.9 — YYYY-MM-DD` section to `CHANGELOG.md`.
3. Merge the validated change into `main`.

The main build detects that `v0.7.9` and its GitHub Release do not exist. After all build, test, smoke-test and image steps succeed, it:

- publishes `ghcr.io/juloc/julgate:0.7.9`, `:0.7`, `:latest` and `:sha-...`;
- creates the annotated Git tag `v0.7.9`;
- creates the GitHub Release `Julgate 0.7.9` from the matching changelog section;
- attaches a deployment archive and SHA-256 checksum.

Further commits on `main` with the same `VersionPrefix` publish only `edge` and `sha-...`. Existing stable releases and stable package tags are not replaced.

The workflow fails closed when the version is not stable SemVer, the changelog section is missing, or an existing version tag points to another commit.
''',
    encoding="utf-8",
)
