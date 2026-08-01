from pathlib import Path
import codecs

ROOT = Path(__file__).resolve().parents[1]


def read_utf8(path: Path) -> tuple[str, bool]:
    data = path.read_bytes()
    return data.decode("utf-8-sig"), data.startswith(codecs.BOM_UTF8)


def write_utf8(path: Path, text: str, had_bom: bool) -> None:
    data = text.encode("utf-8")
    if had_bom:
        data = codecs.BOM_UTF8 + data
    path.write_bytes(data)


html_path = ROOT / "Matgate" / "Web" / "HtmlViews.cs"
html, html_bom = read_utf8(html_path)
old_success = '''                {{(passwordChanged
                    ? $"<p class="success">{(german ? "Das Passwort wurde geändert." : "The password was changed.")}</p>"
                    : "")}}
'''
new_success = '''                <p class="success{{(passwordChanged ? "" : " hidden")}}">
                    {{(german ? "Das Passwort wurde geändert." : "The password was changed.")}}
                </p>
'''
if html.count(old_success) != 1:
    raise RuntimeError("Expected one invalid password success message block.")
html = html.replace(old_success, new_success, 1)
write_utf8(html_path, html, html_bom)

test_path = ROOT / "Matgate.Tests" / "AccountPasswordChangeIntegrationTests.cs"
test_path.write_text(
    '''using Matgate.Services;
using Xunit;

namespace Matgate.Tests;

public sealed class AccountPasswordChangeIntegrationTests
{
    [Fact]
    public void AccountPasswordChange_IsIntegratedWithCurrentSecurityBoundaries()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var endpointSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "Matgate", "Web", "EndpointMapping.cs"));
        var viewSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "Matgate", "Web", "HtmlViews.cs"));

        Assert.Contains("/account/password", endpointSource, StringComparison.Ordinal);
        Assert.Contains("ValidateCsrf(context, form)", endpointSource, StringComparison.Ordinal);
        Assert.Contains("hasher.Verify(currentPassword, user.PasswordHash)", endpointSource, StringComparison.Ordinal);
        Assert.Contains("newPassword.Length < 10", endpointSource, StringComparison.Ordinal);
        Assert.Contains("stored.PasswordHash = hasher.Hash(newPassword)", endpointSource, StringComparison.Ordinal);
        Assert.Contains("/account/password", viewSource, StringComparison.Ordinal);
        Assert.Contains("current-password", viewSource, StringComparison.Ordinal);
        Assert.Contains("new-password", viewSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordHasher_VerifiesOnlyTheNewPasswordHash()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("new-secure-password");

        Assert.True(hasher.Verify("new-secure-password", hash));
        Assert.False(hasher.Verify("old-secure-password", hash));
    }
}
''',
    encoding="utf-8",
    newline="\n",
)
