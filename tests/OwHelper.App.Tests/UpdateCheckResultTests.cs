using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OwHelper.App.Tests;

public class UpdateCheckResultTests
{
    sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (ResponseFactory != null)
            {
                return Task.FromResult(ResponseFactory(request));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    const string ValidReleaseJson = """
    {
        "tag_name": "v1.2.0",
        "name": "v1.2.0",
        "body": "Release notes",
        "draft": false,
        "prerelease": false,
        "assets": [
            {
                "id": 101,
                "name": "OwHelper-Setup-1.2.0.exe",
                "browser_download_url": "https://github.com/4iKZ/ow_helper/releases/download/v1.2.0/OwHelper-Setup-1.2.0.exe",
                "size": 50000000,
                "digest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
            }
        ]
    }
    """;

    [Fact]
    public async Task CheckForUpdates_200Success_ReturnsUpdateAvailable()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseFactory = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidReleaseJson)
            }
        };
        using var client = new HttpClient(handler);
        var service = new UpdateService(client);

        var result = await service.CheckForUpdatesAsync("1.1.0");

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Update);
        Assert.Equal("1.2.0", result.Update.Version);
    }

    [Fact]
    public async Task CheckForUpdates_403RateLimit_ReturnsRateLimited()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseFactory = req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("rate limited")
                };
                resp.Headers.Add("X-RateLimit-Remaining", "0");
                return resp;
            }
        };
        using var client = new HttpClient(handler);
        var service = new UpdateService(client);

        var result = await service.CheckForUpdatesAsync("1.1.0");

        Assert.Equal(UpdateCheckStatus.RateLimited, result.Status);
        Assert.Contains("访问限制", result.Message);
    }

    [Fact]
    public async Task CheckForUpdates_403Other_ReturnsApiError()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseFactory = req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("forbidden")
                };
                resp.Headers.Add("X-RateLimit-Remaining", "50");
                return resp;
            }
        };
        using var client = new HttpClient(handler);
        var service = new UpdateService(client);

        var result = await service.CheckForUpdatesAsync("1.1.0");

        Assert.Equal(UpdateCheckStatus.ApiError, result.Status);
        Assert.Contains("暂时异常", result.Message);
    }

    [Fact]
    public async Task CheckForUpdates_429_ReturnsRateLimited()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseFactory = req => new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("Too many requests")
            }
        };
        using var client = new HttpClient(handler);
        var service = new UpdateService(client);

        var result = await service.CheckForUpdatesAsync("1.1.0");

        Assert.Equal(UpdateCheckStatus.RateLimited, result.Status);
    }

    [Fact]
    public async Task CheckForUpdates_500_ReturnsApiError()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseFactory = req => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server Error")
            }
        };
        using var client = new HttpClient(handler);
        var service = new UpdateService(client);

        var result = await service.CheckForUpdatesAsync("1.1.0");

        Assert.Equal(UpdateCheckStatus.ApiError, result.Status);
    }

    [Fact]
    public async Task CheckForUpdates_NetworkException_ReturnsNetworkError()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseFactory = req => throw new HttpRequestException("Name resolution failed")
        };
        using var client = new HttpClient(handler);
        var service = new UpdateService(client);

        var result = await service.CheckForUpdatesAsync("1.1.0");

        Assert.Equal(UpdateCheckStatus.NetworkError, result.Status);
        Assert.Contains("无法连接更新服务器", result.Message);
    }

    [Fact]
    public async Task CheckForUpdates_InvalidJson_ReturnsInvalidResponse()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseFactory = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Not JSON at all")
            }
        };
        using var client = new HttpClient(handler);
        var service = new UpdateService(client);

        var result = await service.CheckForUpdatesAsync("1.1.0");

        Assert.Equal(UpdateCheckStatus.InvalidResponse, result.Status);
        Assert.Contains("格式异常", result.Message);
    }

    [Fact]
    public async Task CheckForUpdates_DeduplicatesConcurrentAndCachedChecks()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseFactory = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidReleaseJson)
            }
        };
        using var client = new HttpClient(handler);
        var service = new UpdateService(client);

        var first = await service.CheckForUpdatesAsync("1.1.0", force: false);
        var second = await service.CheckForUpdatesAsync("1.1.0", force: false);

        Assert.Equal(1, handler.CallCount);
        Assert.Same(first, second);

        var forced = await service.CheckForUpdatesAsync("1.1.0", force: true);
        Assert.Equal(2, handler.CallCount);
    }
}
