using Xunit;

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
