from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HTML_VIEWS = ROOT / "Matgate" / "Web" / "HtmlViews.cs"


def replace_once(text: str, old: str, new: str, description: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one {description}, found {count}.")
    return text.replace(old, new, 1)


source = HTML_VIEWS.read_text(encoding="utf-8-sig")

source = replace_once(
    source,
    "                            if (tab.oskInput) {",
    "                            if (tab.inputSink) {",
    "mobile keyboard action condition",
)

source = replace_once(
    source,
    """                                        if (document.activeElement === tab.oskInput) {
                                            tab.oskInput.blur();
                                        }
                                        else {
                                            tab.oskInput.focus();
                                        }""",
    """                                        const inputElement = tab.inputSink.getElement();
                                        if (document.activeElement === inputElement) {
                                            inputElement.blur();
                                        }
                                        else {
                                            tab.inputSink.focus();
                                        }""",
    "mobile keyboard action callback",
)

start_marker = """                        // Hidden input to raise the device's native keyboard on touch devices and forward
                        // typed characters to the remote as key events (reliable across mobile browsers)."""
end_marker = "\n\n                        tab.keyboard = new Guacamole.Keyboard(tab.panel);"
start = source.find(start_marker)
if start < 0:
    raise RuntimeError("Legacy mobile keyboard block start was not found.")
end = source.find(end_marker, start)
if end < 0:
    raise RuntimeError("Legacy mobile keyboard block end was not found.")

input_sink_block = """                        // Guacamole.InputSink is the single mobile text-input source. Its events
                        // bubble through the same Guacamole.Keyboard instance as hardware keys,
                        // preventing Android IME input from being transmitted twice.
                        if (isTouchDevice) {
                            tab.inputSink?.getElement().remove();

                            const inputSink = new Guacamole.InputSink();
                            const inputElement = inputSink.getElement();
                            inputElement.classList.add('osk-input');
                            inputElement.setAttribute('aria-hidden', 'true');
                            tab.panel.appendChild(inputElement);
                            tab.inputSink = inputSink;
                        }"""
source = source[:start] + input_sink_block + source[end:]

for forbidden in (
    "oskInput.addEventListener('beforeinput'",
    "const sendKeysym = keysym =>",
    "tab.oskInput",
):
    if forbidden in source:
        raise RuntimeError(f"Legacy mobile input path remains: {forbidden}")

for required in (
    "new Guacamole.InputSink()",
    "tab.inputSink.focus();",
    "new Guacamole.Keyboard(tab.panel)",
):
    if required not in source:
        raise RuntimeError(f"Required mobile input integration is missing: {required}")

HTML_VIEWS.write_text(source, encoding="utf-8-sig", newline="\n")

regression_test = ROOT / "Matgate.Tests" / "MobileKeyboardIntegrationTests.cs"
regression_test.write_text(
    """using Xunit;

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
""",
    encoding="utf-8",
    newline="\n",
)

project_file = ROOT / "Matgate" / "Matgate.csproj"
project = project_file.read_text(encoding="utf-8")
project = replace_once(
    project,
    "<VersionPrefix>0.7.6</VersionPrefix>",
    "<VersionPrefix>0.7.7</VersionPrefix>",
    "project version",
)
project_file.write_text(project, encoding="utf-8", newline="\n")

env_file = ROOT / ".env.example"
env = env_file.read_text(encoding="utf-8")
env = replace_once(env, "JULGATE_VERSION=0.7.6", "JULGATE_VERSION=0.7.7", "example image version")
env_file.write_text(env, encoding="utf-8", newline="\n")

readme_file = ROOT / "README.md"
readme = readme_file.read_text(encoding="utf-8")
readme = replace_once(
    readme,
    """0.7.0
ghcr.io/juloc/julgate:0.7.0""",
    """0.7.7
ghcr.io/juloc/julgate:0.7.7""",
    "README current release",
)
readme_file.write_text(readme, encoding="utf-8", newline="\n")

for compose_file in ROOT.glob("docker-compose*.yaml"):
    compose = compose_file.read_text(encoding="utf-8")
    updated = compose.replace("JULGATE_VERSION:-0.7.0", "JULGATE_VERSION:-0.7.7")
    updated = updated.replace("JULGATE_VERSION:-0.7.6", "JULGATE_VERSION:-0.7.7")
    if updated != compose:
        compose_file.write_text(updated, encoding="utf-8", newline="\n")

changelog_file = ROOT / "CHANGELOG.md"
changelog = changelog_file.read_text(encoding="utf-8")
entry = """## 0.7.7 — 2026-08-01

### Remote sessions

- Replace the custom Android `beforeinput` forwarding path with Guacamole 1.6 `InputSink`.
- Route native mobile and hardware keyboard input through one `Guacamole.Keyboard` instance so RDP characters are sent exactly once.
- Add a regression test that rejects the former duplicate mobile input path.

### Upstream

- Recheck `Real-TTX/Matgate` at `8f27b00585f44e68e6867a1b5a21eb08cc32f441`; no newer upstream commits require integration.
- Keep selective upstream synchronization instead of merging security, credential or deployment code over Julgate.

"""
changelog = replace_once(changelog, "# Changelog\n\n", "# Changelog\n\n" + entry, "changelog heading")
changelog_file.write_text(changelog, encoding="utf-8", newline="\n")
