using Xunit;

namespace Matgate.Tests;

public sealed class MobileKeyboardIntegrationTests
{
    [Fact]
    public void SessionShell_UsesOneGuacamoleInputPipelineForMobileKeyboard()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var sourcePath = Path.Combine(repositoryRoot, "Matgate", "Web", "HtmlViews.cs");

        Assert.True(File.Exists(sourcePath), $"Source file not found: {sourcePath}");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("new Guacamole.InputSink()", source, StringComparison.Ordinal);
        Assert.Contains("new Guacamole.Keyboard(tab.panel)", source, StringComparison.Ordinal);
        Assert.Contains("tab.inputSink.focus();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("oskInput.addEventListener('beforeinput'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("const sendKeysym = keysym =>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tab.oskInput", source, StringComparison.Ordinal);
    }
}
