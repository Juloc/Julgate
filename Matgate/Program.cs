using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Matgate.Services;
using Matgate.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);
var configuredDataDirectory = GetEnvironmentValue("JULGATE_DATA_DIR", "MATGATE_DATA_DIR")
    ?? builder.Configuration["Matgate:DataDirectory"];
var dataDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredDataDirectory)
    ? Path.Combine(builder.Environment.ContentRootPath, "data")
    : configuredDataDirectory);
var keyDirectory = Path.Combine(dataDirectory, "keys");
var maxRequestBodySize = ReadPositiveLong("JULGATE_MAX_REQUEST_BODY_BYTES", 512L * 1024L * 1024L);
var sessionHours = Math.Clamp(ReadPositiveInt("JULGATE_SESSION_HOURS", 8), 1, 24);
var requireSecureCookies = ReadBoolean("JULGATE_REQUIRE_SECURE_COOKIES", !builder.Environment.IsDevelopment());
var trustForwardedHeaders = ReadBoolean("JULGATE_TRUST_FORWARD_HEADERS", true);
var enableWebsiteProxy = ReadBoolean("JULGATE_ENABLE_WEBSITE_PROXY", false);
var enableNetworkTools = ReadBoolean("JULGATE_ENABLE_NETWORK_TOOLS", false);

Directory.CreateDirectory(keyDirectory);
SetPrivateDirectoryPermissions(dataDirectory);
SetPrivateDirectoryPermissions(keyDirectory);
ValidateGuacamoleSecret(builder.Environment);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxRequestBodySize;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxRequestBodySize;
    options.BufferBody = false;
});

if (trustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".Julgate.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = requireSecureCookies
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(sessionHours);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/forbidden";
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var isLoginAttempt = HttpMethods.IsPost(context.Request.Method)
            && string.Equals(context.Request.Path.Value, "/login", StringComparison.OrdinalIgnoreCase);
        var partition = $"{context.Connection.RemoteIpAddress ?? IPAddress.None}:{(isLoginAttempt ? "login" : "global")}";

        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = isLoginAttempt ? 10 : 240,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    });
    options.OnRejected = static async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync("Too many requests.", cancellationToken);
    };
});

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .SetApplicationName("Julgate");
builder.Services.AddAuthorization();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<JsonDataStore>();
builder.Services.AddSingleton<GuacamoleConfigWriter>();
builder.Services.AddSingleton<HtmlViews>();
builder.Services.AddSingleton<GuacamoleLauncher>();
builder.Services.AddSingleton<NetworkToolsService>();
builder.Services.AddSingleton<IFileGatewayService, FileGatewayService>();
builder.Services.AddSingleton<WorkspaceService>();
builder.Services.AddSingleton<WebsiteProxyService>();

var app = builder.Build();

if (trustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseStaticFiles();
app.UseMiddleware<Ae01ThemeMiddleware>();

app.Use(async (context, next) =>
{
    var isWebsitePath = context.Request.Path.StartsWithSegments("/website");
    var isNetworkToolsPath = context.Request.Path.StartsWithSegments("/tools")
        || context.Request.Path.StartsWithSegments("/api/tools");
    var isGatewayContent = context.Request.Path.StartsWithSegments("/guacamole") || isWebsitePath;

    if (isWebsitePath && !enableWebsiteProxy)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("Website proxy is disabled.");
        return;
    }

    if (isNetworkToolsPath && !enableNetworkTools)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("Network tools are disabled.");
        return;
    }

    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        if (context.Request.IsHttps)
        {
            context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        if (!isGatewayContent)
        {
            context.Response.Headers["Referrer-Policy"] = "same-origin";
            context.Response.Headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=(), payment=(), usb=()";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
                "img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; " +
                "script-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:; frame-src 'self'; form-action 'self'";
        }

        return Task.CompletedTask;
    });

    if (IsUnsafeMethod(context.Request.Method)
        && !IsSameOriginRequest(context.Request))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Cross-origin state-changing requests are not allowed.");
        return;
    }

    await next();
});

app.UseRouting();
app.UseRateLimiter();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});
app.UseAuthentication();
app.UseAuthorization();

var hasher = app.Services.GetRequiredService<PasswordHasher>();
var store = app.Services.GetRequiredService<JsonDataStore>();
await store.EnsureSeedAdminAsync(hasher, app.Logger, app.Lifetime.ApplicationStopping);
await store.EnsureGuacamoleSecretsAsync(hasher, app.Lifetime.ApplicationStopping);
await store.EnsureWorkspacePublicAccessDefaultsAsync(TimeSpan.FromHours(24), app.Lifetime.ApplicationStopping);
await store.RemoveLegacyGatewayServersAsync(app.Lifetime.ApplicationStopping);
await app.Services.GetRequiredService<GuacamoleConfigWriter>()
    .SynchronizeAsync(app.Lifetime.ApplicationStopping);

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", service = "julgate" }));
app.MapMatgateEndpoints();

await app.RunAsync();

static bool IsUnsafeMethod(string method)
{
    return HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);
}

static bool IsSameOriginRequest(HttpRequest request)
{
    var origin = request.Headers.Origin.ToString();
    if (!string.IsNullOrWhiteSpace(origin))
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
            && string.Equals(originUri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    var referer = request.Headers.Referer.ToString();
    if (!string.IsNullOrWhiteSpace(referer))
    {
        return Uri.TryCreate(referer, UriKind.Absolute, out var refererUri)
            && string.Equals(refererUri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    return true;
}

static void ValidateGuacamoleSecret(IHostEnvironment environment)
{
    if (environment.IsDevelopment())
    {
        return;
    }

    var secret = GetEnvironmentValue(
        "JULGATE_GUACAMOLE_JSON_SECRET_KEY",
        "MATGATE_GUACAMOLE_JSON_SECRET_KEY",
        "Guacamole__JsonSecretKey");

    var isHex = !string.IsNullOrWhiteSpace(secret)
        && secret.Length == 32
        && secret.All(Uri.IsHexDigit);
    var isKnownDefault = string.Equals(secret, "0123456789abcdeffedcba9876543210", StringComparison.OrdinalIgnoreCase);

    if (!isHex || isKnownDefault)
    {
        throw new InvalidOperationException(
            "Set JULGATE_GUACAMOLE_JSON_SECRET_KEY to a unique random 32-character hexadecimal value.");
    }
}

static string? GetEnvironmentValue(params string[] names)
{
    foreach (var name in names)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    return null;
}

static bool ReadBoolean(string name, bool fallback)
{
    return bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
}

static int ReadPositiveInt(string name, int fallback)
{
    return int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;
}

static long ReadPositiveLong(string name, long fallback)
{
    return long.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;
}

static void SetPrivateDirectoryPermissions(string path)
{
    try
    {
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    catch (PlatformNotSupportedException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}
