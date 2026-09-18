using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OwHelper.App.Tests;

public class UpdateIntegrityTests : IDisposable
{
    readonly string testDir;

    public UpdateIntegrityTests()
    {
        testDir = Path.Combine(Path.GetTempPath(), "OwHelper_UpdateIntegrityTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    sealed class MockDownloadHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handler(request));
        }
    }

    static (byte[] Bytes, string Sha256, long Size) CreateSamplePayload(string content = "OwHelper Installer Sample Binary Content 1.2.0")
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        byte[] hash = SHA256.HashData(bytes);
        string sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        return (bytes, sha256, bytes.Length);
    }

    [Fact]
    public async Task DownloadAndVerify_ValidSizeAndHash_SucceedsAndReturnsVerifiedPackage()
    {
        var sample = CreateSamplePayload();
        string destPath = Path.Combine(testDir, "OwHelper-Setup-1.2.0.exe");
        string logPath = Path.Combine(testDir, "test.log");
        var log = new AppLog(logPath);

        var handler = new MockDownloadHandler
        {
            Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sample.Bytes)
            }
        };

        using var client = new HttpClient(handler);
        var service = new UpdateService(client, log);

        var info = new UpdateInfo(
            Version: "1.2.0",
            TagName: "v1.2.0",
            Title: "v1.2.0",
            ReleaseNotes: "Notes",
            SetupDownloadUrl: "https://github.com/4iKZ/ow_helper/releases/download/v1.2.0/OwHelper-Setup-1.2.0.exe",
            SetupSha256: sample.Sha256,
            SetupSizeBytes: sample.Size,
            SetupAssetId: 101,
            ZipDownloadUrl: "",
            PublishedAt: DateTimeOffset.UtcNow,
            HtmlUrl: ""
        );

        VerifiedUpdatePackage package = await service.DownloadAndVerifyUpdateAsync(info, destPath);

        Assert.True(package.Verified);
        Assert.Equal(destPath, package.FilePath);
        Assert.Equal(sample.Size, package.SizeBytes);
        Assert.Equal(sample.Sha256, package.Sha256);
        Assert.True(File.Exists(destPath));
        Assert.False(File.Exists(destPath + ".downloading"));

        string logText = File.ReadAllText(logPath);
        Assert.Contains("UPDATE_DOWNLOAD_START", logText);
        Assert.Contains("UPDATE_DOWNLOAD_COMPLETE", logText);
        Assert.Contains("UPDATE_VERIFY_OK", logText);
    }

    [Fact]
    public async Task DownloadAndVerify_SizeMismatch_AbortsAndCleansUpTemp()
    {
        var sample = CreateSamplePayload();
        string destPath = Path.Combine(testDir, "OwHelper-Setup-1.2.0.exe");
        string logPath = Path.Combine(testDir, "test.log");
        var log = new AppLog(logPath);

        var handler = new MockDownloadHandler
        {
            Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sample.Bytes)
            }
        };

        using var client = new HttpClient(handler);
        var service = new UpdateService(client, log);

        var info = new UpdateInfo(
            Version: "1.2.0",
            TagName: "v1.2.0",
            Title: "v1.2.0",
            ReleaseNotes: "Notes",
            SetupDownloadUrl: "https://github.com/4iKZ/ow_helper/releases/download/v1.2.0/OwHelper-Setup-1.2.0.exe",
            SetupSha256: sample.Sha256,
            SetupSizeBytes: sample.Size + 999, // 故意让大小不匹配
            SetupAssetId: 101,
            ZipDownloadUrl: "",
            PublishedAt: DateTimeOffset.UtcNow,
            HtmlUrl: ""
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadAndVerifyUpdateAsync(info, destPath));

        Assert.Contains("大小不匹配", ex.Message);
        Assert.False(File.Exists(destPath));
        Assert.False(File.Exists(destPath + ".downloading"));

        string logText = File.ReadAllText(logPath);
        Assert.Contains("UPDATE_VERIFY_SIZE_MISMATCH", logText);
        Assert.Contains("UPDATE_INSTALL_ABORTED", logText);
    }

    [Fact]
    public async Task DownloadAndVerify_HashMismatch_AbortsWithAuditLog()
    {
        var sample = CreateSamplePayload();
        string destPath = Path.Combine(testDir, "OwHelper-Setup-1.2.0.exe");
        string logPath = Path.Combine(testDir, "test.log");
        var log = new AppLog(logPath);

        var handler = new MockDownloadHandler
        {
            Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sample.Bytes)
            }
        };

        using var client = new HttpClient(handler);
        var service = new UpdateService(client, log);

        string wrongSha = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        var info = new UpdateInfo(
            Version: "1.2.0",
            TagName: "v1.2.0",
            Title: "v1.2.0",
            ReleaseNotes: "Notes",
            SetupDownloadUrl: "https://github.com/4iKZ/ow_helper/releases/download/v1.2.0/OwHelper-Setup-1.2.0.exe",
            SetupSha256: wrongSha,
            SetupSizeBytes: sample.Size,
            SetupAssetId: 101,
            ZipDownloadUrl: "",
            PublishedAt: DateTimeOffset.UtcNow,
            HtmlUrl: ""
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadAndVerifyUpdateAsync(info, destPath));

        Assert.Contains("哈希不匹配", ex.Message);
        Assert.False(File.Exists(destPath));
        Assert.False(File.Exists(destPath + ".downloading"));

        string logText = File.ReadAllText(logPath);
        Assert.Contains("UPDATE_VERIFY_HASH_MISMATCH", logText);
        Assert.Contains("expectedSha256=" + wrongSha, logText);
        Assert.Contains("actualSha256=" + sample.Sha256, logText);
        Assert.Contains("sourceHost=github.com", logText);
        Assert.Contains("UPDATE_INSTALL_ABORTED", logText);
    }

    [Fact]
    public async Task DownloadAndVerify_GitHubFails_ProxySucceeds()
    {
        var sample = CreateSamplePayload();
        string destPath = Path.Combine(testDir, "OwHelper-Setup-1.2.0.exe");
        string logPath = Path.Combine(testDir, "test.log");
        var log = new AppLog(logPath);

        var handler = new MockDownloadHandler
        {
            Handler = req =>
            {
                if (req.RequestUri!.Host == "github.com")
                {
                    return new HttpResponseMessage(HttpStatusCode.BadGateway);
                }
                if (req.RequestUri.Host == "ghproxy.net")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(sample.Bytes)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        using var client = new HttpClient(handler);
        var service = new UpdateService(client, log);

        var info = new UpdateInfo(
            Version: "1.2.0",
            TagName: "v1.2.0",
            Title: "v1.2.0",
            ReleaseNotes: "Notes",
            SetupDownloadUrl: "https://github.com/4iKZ/ow_helper/releases/download/v1.2.0/OwHelper-Setup-1.2.0.exe",
            SetupSha256: sample.Sha256,
            SetupSizeBytes: sample.Size,
            SetupAssetId: 101,
            ZipDownloadUrl: "",
            PublishedAt: DateTimeOffset.UtcNow,
            HtmlUrl: ""
        );

        VerifiedUpdatePackage package = await service.DownloadAndVerifyUpdateAsync(info, destPath);

        Assert.True(package.Verified);
        Assert.Contains("ghproxy.net", package.SourceUrl);
        Assert.True(File.Exists(destPath));

        string logText = File.ReadAllText(logPath);
        Assert.Contains("sourceHost=github.com", logText);
        Assert.Contains("UPDATE_DOWNLOAD_SOURCE_FAILED", logText);
        Assert.Contains("sourceHost=ghproxy.net", logText);
        Assert.Contains("UPDATE_VERIFY_OK", logText);
    }

    [Fact]
    public async Task DownloadAndVerify_GitHubHashMismatch_ProxySucceeds()
    {
        var sample = CreateSamplePayload("Good payload");
        var badSample = CreateSamplePayload("Bad corrupt payload");
        string destPath = Path.Combine(testDir, "OwHelper-Setup-1.2.0.exe");
        string logPath = Path.Combine(testDir, "test.log");
        var log = new AppLog(logPath);

        var handler = new MockDownloadHandler
        {
            Handler = req =>
            {
                if (req.RequestUri!.Host == "github.com")
                {
                    // GitHub 返回了被篡改/大小一致但内容错误的数据
                    byte[] corrupt = new byte[sample.Size];
                    Array.Copy(badSample.Bytes, corrupt, Math.Min(badSample.Bytes.Length, sample.Size));
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(corrupt)
                    };
                }
                if (req.RequestUri.Host == "ghproxy.net")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(sample.Bytes)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        using var client = new HttpClient(handler);
        var service = new UpdateService(client, log);

        var info = new UpdateInfo(
            Version: "1.2.0",
            TagName: "v1.2.0",
            Title: "v1.2.0",
            ReleaseNotes: "Notes",
            SetupDownloadUrl: "https://github.com/4iKZ/ow_helper/releases/download/v1.2.0/OwHelper-Setup-1.2.0.exe",
            SetupSha256: sample.Sha256,
            SetupSizeBytes: sample.Size,
            SetupAssetId: 101,
            ZipDownloadUrl: "",
            PublishedAt: DateTimeOffset.UtcNow,
            HtmlUrl: ""
        );

        VerifiedUpdatePackage package = await service.DownloadAndVerifyUpdateAsync(info, destPath);

        Assert.True(package.Verified);
        Assert.Contains("ghproxy.net", package.SourceUrl);
        Assert.True(File.Exists(destPath));

        string logText = File.ReadAllText(logPath);
        Assert.Contains("UPDATE_VERIFY_HASH_MISMATCH", logText);
        Assert.Contains("UPDATE_VERIFY_OK", logText);
    }

    [Fact]
    public async Task DownloadAndVerify_UntrustedHost_ThrowsImmediately()
    {
        var sample = CreateSamplePayload();
        string destPath = Path.Combine(testDir, "OwHelper-Setup-1.2.0.exe");
        var handler = new MockDownloadHandler();
        using var client = new HttpClient(handler);
        var service = new UpdateService(client);

        var info = new UpdateInfo(
            Version: "1.2.0",
            TagName: "v1.2.0",
            Title: "v1.2.0",
            ReleaseNotes: "Notes",
            SetupDownloadUrl: "http://malicious.site/OwHelper-Setup.exe",
            SetupSha256: sample.Sha256,
            SetupSizeBytes: sample.Size,
            SetupAssetId: 101,
            ZipDownloadUrl: "",
            PublishedAt: DateTimeOffset.UtcNow,
            HtmlUrl: ""
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadAndVerifyUpdateAsync(info, destPath));

        Assert.Contains("不受信任", ex.Message);
        Assert.False(File.Exists(destPath));
    }

    [Fact]
    public async Task DownloadAndVerify_InvalidOrMissingSha256_ThrowsImmediately()
    {
        var sample = CreateSamplePayload();
        string destPath = Path.Combine(testDir, "OwHelper-Setup-1.2.0.exe");
        var handler = new MockDownloadHandler();
        using var client = new HttpClient(handler);
        var service = new UpdateService(client);

        var info = new UpdateInfo(
            Version: "1.2.0",
            TagName: "v1.2.0",
            Title: "v1.2.0",
            ReleaseNotes: "Notes",
            SetupDownloadUrl: "https://github.com/4iKZ/ow_helper/releases/download/v1.2.0/OwHelper-Setup-1.2.0.exe",
            SetupSha256: "not-a-valid-sha256",
            SetupSizeBytes: sample.Size,
            SetupAssetId: 101,
            ZipDownloadUrl: "",
            PublishedAt: DateTimeOffset.UtcNow,
            HtmlUrl: ""
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadAndVerifyUpdateAsync(info, destPath));

        Assert.Contains("SHA-256", ex.Message);
        Assert.False(File.Exists(destPath));
    }

    [Fact]
    public async Task DownloadAndVerify_Cancellation_CleansUpTempFile()
    {
        var sample = CreateSamplePayload();
        string destPath = Path.Combine(testDir, "OwHelper-Setup-1.2.0.exe");
        using var cts = new CancellationTokenSource();

        var handler = new MockDownloadHandler
        {
            Handler = req =>
            {
                cts.Cancel(); // 模拟在下载过程中取消
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(sample.Bytes)
                };
            }
        };

        using var client = new HttpClient(handler);
        var service = new UpdateService(client);

        var info = new UpdateInfo(
            Version: "1.2.0",
            TagName: "v1.2.0",
            Title: "v1.2.0",
            ReleaseNotes: "Notes",
            SetupDownloadUrl: "https://github.com/4iKZ/ow_helper/releases/download/v1.2.0/OwHelper-Setup-1.2.0.exe",
            SetupSha256: sample.Sha256,
            SetupSizeBytes: sample.Size,
            SetupAssetId: 101,
            ZipDownloadUrl: "",
            PublishedAt: DateTimeOffset.UtcNow,
            HtmlUrl: ""
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DownloadAndVerifyUpdateAsync(info, destPath, ct: cts.Token));

        Assert.False(File.Exists(destPath));
        Assert.False(File.Exists(destPath + ".downloading"));
    }
}
