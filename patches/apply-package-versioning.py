from pathlib import Path

# The workflow file now exists; this commit triggers the migration job.
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

old_compute = '''          build_time="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
          publish=false

          if [[ "${GITHUB_REF}" == refs/tags/v* ]]; then
            version="${GITHUB_REF_NAME#v}"
            if [[ "$version" != "$base_version" ]]; then
              echo "Tag v$version does not match VersionPrefix $base_version."
              exit 1
            fi
            publish=true
          elif [[ "${GITHUB_REF}" == "refs/heads/main" ]]; then
            version="$base_version"
            publish=true
          else
            version="${base_version}-ci.${GITHUB_RUN_NUMBER}"
          fi

          echo "version=$version" >> "$GITHUB_OUTPUT"
          echo "build_time=$build_time" >> "$GITHUB_OUTPUT"
          echo "publish=$publish" >> "$GITHUB_OUTPUT"
'''

new_compute = '''          if [[ ! "$base_version" =~ ^([0-9]+)\\.([0-9]+)\\.([0-9]+)$ ]]; then
            echo "VersionPrefix must be a stable semantic version such as 1.2.3."
            exit 1
          fi

          major_minor="${BASH_REMATCH[1]}.${BASH_REMATCH[2]}"
          build_time="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
          publish=false
          channel=ci

          if [[ "${GITHUB_REF}" == refs/tags/v* ]]; then
            version="${GITHUB_REF_NAME#v}"
            if [[ "$version" != "$base_version" ]]; then
              echo "Tag v$version does not match VersionPrefix $base_version."
              exit 1
            fi
            publish=true
            channel=release
          elif [[ "${GITHUB_REF}" == "refs/heads/main" ]]; then
            version="${base_version}-edge.${GITHUB_RUN_NUMBER}"
            publish=true
            channel=edge
          else
            version="${base_version}-ci.${GITHUB_RUN_NUMBER}"
          fi

          echo "version=$version" >> "$GITHUB_OUTPUT"
          echo "base_version=$base_version" >> "$GITHUB_OUTPUT"
          echo "major_minor=$major_minor" >> "$GITHUB_OUTPUT"
          echo "channel=$channel" >> "$GITHUB_OUTPUT"
          echo "build_time=$build_time" >> "$GITHUB_OUTPUT"
          echo "publish=$publish" >> "$GITHUB_OUTPUT"
'''
workflow = replace_once(workflow, old_compute, new_compute, "version computation block")

old_tags = '''          tags: |
            type=raw,value=${{ steps.version.outputs.version }}
            type=sha,prefix=sha-
            type=raw,value=latest,enable=${{ github.ref == 'refs/heads/main' }}
'''

new_tags = '''          tags: |
            type=raw,value=${{ steps.version.outputs.base_version }},enable=${{ steps.version.outputs.channel == 'release' }}
            type=raw,value=${{ steps.version.outputs.major_minor }},enable=${{ steps.version.outputs.channel == 'release' }}
            type=raw,value=latest,enable=${{ steps.version.outputs.channel == 'release' }}
            type=raw,value=edge,enable=${{ steps.version.outputs.channel == 'edge' }}
            type=sha,prefix=sha-
'''
workflow = replace_once(workflow, old_tags, new_tags, "Docker metadata tag block")
write_utf8(workflow_path, workflow, workflow_bom)

readme_path = ROOT / "README.md"
readme, readme_bom = read_utf8(readme_path)
readme = replace_once(
    readme,
    "Production Compose files use the immutable version tag by default. The release workflow also publishes `latest` and `sha-<commit>`.\n",
    "Production Compose files use the immutable version tag by default. A Git tag such as `v0.7.8` publishes `0.7.8`, the compatible `0.7` tag, `latest` and `sha-<commit>`. The `main` branch publishes only `edge` and `sha-<commit>`, so stable package versions are never overwritten.\n",
    "README package versioning description",
)
write_utf8(readme_path, readme, readme_bom)

test_path = ROOT / "Matgate.Tests" / "PackageVersioningWorkflowTests.cs"
test_path.write_text(
    '''using Xunit;

namespace Matgate.Tests;

public sealed class PackageVersioningWorkflowTests
{
    [Fact]
    public void ContainerWorkflow_SeparatesEdgeAndStablePackageTags()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "docker-image.yml"));

        Assert.Contains("channel=release", workflow, StringComparison.Ordinal);
        Assert.Contains("channel=edge", workflow, StringComparison.Ordinal);
        Assert.Contains("version=\"${base_version}-edge.${GITHUB_RUN_NUMBER}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("type=raw,value=edge", workflow, StringComparison.Ordinal);
        Assert.Contains("steps.version.outputs.base_version", workflow, StringComparison.Ordinal);
        Assert.Contains("steps.version.outputs.major_minor", workflow, StringComparison.Ordinal);
        Assert.Contains("steps.version.outputs.channel == 'release'", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("type=raw,value=${{ steps.version.outputs.version }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("enable=${{ github.ref == 'refs/heads/main' }}", workflow, StringComparison.Ordinal);
    }
}
''',
    encoding="utf-8",
)
