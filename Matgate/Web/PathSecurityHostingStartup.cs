using Microsoft.AspNetCore.Hosting;

[assembly: HostingStartup(typeof(Matgate.Web.PathSecurityHostingStartup))]

namespace Matgate.Web;

public sealed class PathSecurityHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddTransient<IStartupFilter, PathSecurityStartupFilter>();
        });
    }

    private sealed class PathSecurityStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UseMiddleware<PathTraversalGuardMiddleware>();
                app.UseMiddleware<CrossOriginGuardMiddleware>();
                app.UseMiddleware<WebsiteProxyTargetGuardMiddleware>();
                app.UseMiddleware<ArchiveExtractionGuardMiddleware>();
                app.UseMiddleware<NetworkToolsAdminGuardMiddleware>();
                app.UseMiddleware<WorkspaceCookieHardeningMiddleware>();
                next(app);
            };
        }
    }
}
