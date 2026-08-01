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


def replace_once(text: str, old: str, new: str, description: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one {description}, found {count}.")
    return text.replace(old, new, 1)


endpoint_path = ROOT / "Matgate" / "Web" / "EndpointMapping.cs"
endpoint, endpoint_bom = read_utf8(endpoint_path)
endpoint = replace_once(
    endpoint,
    '        app.MapPost("/account", UpdateAccountAsync).RequireAuthorization();\n',
    '        app.MapPost("/account", UpdateAccountAsync).RequireAuthorization();\n'
    '        app.MapPost("/account/password", ChangeOwnPasswordAsync).RequireAuthorization();\n',
    "account password route",
)

method_marker = """    private static async Task<IResult> ToggleFavoriteServerAsync(
"""
change_password_method = """    private static async Task<IResult> ChangeOwnPasswordAsync(
        HttpContext context,
        JsonDataStore store,
        PasswordHasher hasher,
        HtmlViews views)
    {
        var user = await RequireUserAsync(context, store);
        if (user is null)
        {
            return Results.Redirect("/login");
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        if (!ValidateCsrf(context, form))
        {
            return BadRequest(context, user, views);
        }

        var german = HtmlViews.Language(context) == "de";
        var currentPassword = form["currentPassword"].ToString();
        var newPassword = form["newPassword"].ToString();
        var confirmPassword = form["confirmPassword"].ToString();

        string? error = null;
        if (!hasher.Verify(currentPassword, user.PasswordHash))
        {
            error = german
                ? "Das aktuelle Passwort ist falsch."
                : "The current password is incorrect.";
        }
        else if (newPassword.Length < 10)
        {
            error = german
                ? "Das neue Passwort muss mindestens 10 Zeichen lang sein."
                : "The new password must be at least 10 characters long.";
        }
        else if (hasher.Verify(newPassword, user.PasswordHash))
        {
            error = german
                ? "Das neue Passwort muss sich vom aktuellen Passwort unterscheiden."
                : "The new password must differ from the current password.";
        }
        else if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            error = german
                ? "Die neuen Passwörter stimmen nicht überein."
                : "The new passwords do not match.";
        }

        if (error is not null)
        {
            return Results.Content(
                views.Message(
                    context,
                    user,
                    german ? "Passwort ändern" : "Change password",
                    error),
                "text/html");
        }

        await store.UpdateUsersAsync(users =>
        {
            var stored = users.FirstOrDefault(candidate => candidate.Id == user.Id);
            if (stored is null)
            {
                return;
            }

            stored.PasswordHash = hasher.Hash(newPassword);
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, context.RequestAborted);

        return Results.Redirect(EmbedAwareRedirect(context, "/account?passwordChanged=1"));
    }

"""
endpoint = replace_once(
    endpoint,
    method_marker,
    change_password_method + method_marker,
    "account password handler insertion point",
)
write_utf8(endpoint_path, endpoint, endpoint_bom)

html_path = ROOT / "Matgate" / "Web" / "HtmlViews.cs"
html, html_bom = read_utf8(html_path)
account_start = html.find("    public string Account(HttpContext context, MatgateUser user, IReadOnlyList<ServerEndpoint> servers)")
account_end = html.find("    public string WorkspaceCreate(", account_start)
if account_start < 0 or account_end < 0:
    raise RuntimeError("Account view boundaries were not found.")
account = html[account_start:account_end]
account = replace_once(
    account,
    '        var body = $$"""\n',
    '        var german = Language(context) == "de";\n'
    '        var passwordChanged = context.Request.Query["passwordChanged"] == "1";\n'
    '        var body = $$"""\n',
    "account view localization state",
)

favorites_marker = """            <section class="panel">
                <h2>{{T(context, "Favorite servers")}}</h2>
"""
security_panel = """            <section class="panel">
                <h2>{{(german ? "Passwort ändern" : "Change password")}}</h2>
                {{(passwordChanged
                    ? $"<p class=\"success\">{(german ? "Das Passwort wurde geändert." : "The password was changed.")}</p>"
                    : "")}}
                <p class="muted">{{(german ? "Mindestens 10 Zeichen. Das neue Passwort muss sich vom bisherigen unterscheiden." : "At least 10 characters. The new password must differ from the current password.")}}</p>
                <form method="post" action="/account/password" class="form-grid">
                    {{Csrf(context)}}
                    <label>{{(german ? "Aktuelles Passwort" : "Current password")}}
                        <input type="password" name="currentPassword" autocomplete="current-password" required>
                    </label>
                    <label>{{(german ? "Neues Passwort" : "New password")}}
                        <input type="password" name="newPassword" autocomplete="new-password" minlength="10" required>
                    </label>
                    <label>{{(german ? "Neues Passwort bestätigen" : "Confirm new password")}}
                        <input type="password" name="confirmPassword" autocomplete="new-password" minlength="10" required>
                    </label>
                    <div class="actions"><button type="submit" class="primary">{{Icon("key")}}{{(german ? "Passwort ändern" : "Change password")}}</button></div>
                </form>
            </section>
"""
account = replace_once(
    account,
    favorites_marker,
    security_panel + favorites_marker,
    "account favorites panel",
)
html = html[:account_start] + account + html[account_end:]
write_utf8(html_path, html, html_bom)

regression_test = ROOT / "Matgate.Tests" / "AccountPasswordChangeIntegrationTests.cs"
regression_test.write_text(
    """using Matgate.Services;
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
        Assert.Contains("action=\"/account/password\"", viewSource, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"current-password\"", viewSource, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"new-password\"", viewSource, StringComparison.Ordinal);
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
""",
    encoding="utf-8",
    newline="\n",
)

project_path = ROOT / "Matgate" / "Matgate.csproj"
project, project_bom = read_utf8(project_path)
project = replace_once(
    project,
    "<VersionPrefix>0.7.7</VersionPrefix>",
    "<VersionPrefix>0.7.8</VersionPrefix>",
    "project version",
)
write_utf8(project_path, project, project_bom)

for relative_path in (".env.example", "README.md"):
    path = ROOT / relative_path
    text, had_bom = read_utf8(path)
    text = text.replace("0.7.7", "0.7.8")
    write_utf8(path, text, had_bom)

for compose_path in ROOT.glob("docker-compose*.yaml"):
    compose, compose_bom = read_utf8(compose_path)
    updated = compose.replace("JULGATE_VERSION:-0.7.7", "JULGATE_VERSION:-0.7.8")
    if updated != compose:
        write_utf8(compose_path, updated, compose_bom)

changelog_path = ROOT / "CHANGELOG.md"
changelog, changelog_bom = read_utf8(changelog_path)
entry = """## 0.7.8 — 2026-08-01

### Account security

- Selectively port Matgate's self-service password change into Julgate's existing account page.
- Require the current password, CSRF validation, a distinct new password of at least 10 characters and exact confirmation.
- Keep Julgate's current authentication, credential encryption and deployment architecture unchanged.

"""
changelog = replace_once(
    changelog,
    "# Changelog\n\n",
    "# Changelog\n\n" + entry,
    "changelog heading",
)
write_utf8(changelog_path, changelog, changelog_bom)
