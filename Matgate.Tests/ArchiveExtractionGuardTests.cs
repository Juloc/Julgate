using System.Text;
using Matgate.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Matgate.Tests;

public sealed class ArchiveExtractionGuardTests
{
    [Fact]
    public async Task ExplicitExtractEndpoint_IsDetected()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = $"/api/files/{Guid.NewGuid()}/extract";

        Assert.True(await ArchiveExtractionGuardMiddleware.IsExtractionRequestAsync(context.Request));
    }

    [Fact]
    public async Task UploadWithUnzipFlag_IsDetected()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("unzip=true"));

        Assert.True(await ArchiveExtractionGuardMiddleware.IsExtractionRequestAsync(context.Request));
    }

    [Fact]
    public async Task NormalRequest_IsNotBlocked()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/files/server/list";

        Assert.False(await ArchiveExtractionGuardMiddleware.IsExtractionRequestAsync(context.Request));
    }
}
