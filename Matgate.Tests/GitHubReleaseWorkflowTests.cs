using Xunit;

namespace Matgate.Tests;

public sealed class GitHubReleaseWorkflowTests
{
    [Fact]
    public void ReleaseWorkflow_RequiresAllValidationsAndPublishesVersionedArtifacts()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("workflow_run", workflow, StringComparison.Ordinal);
        Assert.Contains("Julgate build", workflow, StringComparison.Ordinal);
        Assert.Contains("Julgate security", workflow, StringComparison.Ordinal);
        Assert.Contains("Julgate completion validation", workflow, StringComparison.Ordinal);
        Assert.Contains("Julgate file protocol integration", workflow, StringComparison.Ordinal);
        Assert.Contains("Julgate operations validation", workflow, StringComparison.Ordinal);
        Assert.Contains("gh api", workflow, StringComparison.Ordinal);
        Assert.Contains("gh release create", workflow, StringComparison.Ordinal);
        Assert.Contains("--target \"${HEAD_SHA}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("type=raw,value=latest", workflow, StringComparison.Ordinal);
        Assert.Contains("julgate-${VERSION}-deployment.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("sha256sum", workflow, StringComparison.Ordinal);
    }
}
