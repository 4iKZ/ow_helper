using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OwHelper;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    UpdateAvailableButManualOnly,
    NetworkError,
    RateLimited,
    ApiError,
    InvalidResponse
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    UpdateInfo? Update,
    string Message,
    int? HttpStatusCode = null);

public sealed record UpdateInfo(
    string Version,
    string TagName,
    string Title,
    string ReleaseNotes,
    string SetupDownloadUrl,
    string SetupSha256,
    long SetupSizeBytes,
    long SetupAssetId,
    string ZipDownloadUrl,
    DateTimeOffset? PublishedAt,
    string HtmlUrl);

public readonly record struct DownloadProgressReport(
    long BytesReceived,
    long TotalBytes,
    double Percent);

public sealed record VerifiedUpdatePackage(
    string FilePath,
    long SizeBytes,
    string Sha256,
    string SourceUrl,
    bool Verified);

public sealed class UpdateService
{
    public const string DefaultOwner = "4iKZ";
    public const string DefaultRepo = "ow_helper";
    static readonly Regex Sha256DigestRegex = new(@"^sha256:([0-9a-fA-F]{64})$", RegexOptions.Compiled);
    static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "ghproxy.net",
        "mirror.ghproxy.com"
    };

    readonly HttpClient httpClient;
    readonly AppLog? log;
    readonly string owner;
    readonly string repo;

    readonly SemaphoreSlim checkGate = new(1, 1);
    UpdateCheckResult? lastCheckResult;
    DateTimeOffset lastCheckAt;

    public UpdateService(HttpClient? httpClient = null, AppLog? log = null, string owner = DefaultOwner, string repo = DefaultRepo)
    {
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        this.log = log;
        this.owner = owner;
        this.repo = repo;
    }

    public static string StandardInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "OW Helper");

    public static string StandardExecutablePath => Path.Combine(StandardInstallDirectory, "OwHelper.Desktop.exe");

    public static bool IsStandardInstalledPath(string? processPath = null)
    {
        string current = processPath ?? Environment.ProcessPath ?? "";
        if (string.IsNullOrWhiteSpace(current)) return false;
        string? currentDir = Path.GetDirectoryName(current);
        if (string.IsNullOrWhiteSpace(currentDir)) return false;

        string normalizedCurrent = Path.GetFullPath(currentDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedStandard = Path.GetFullPath(StandardInstallDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedCurrent, normalizedStandard, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNewerVersion(string latestTagOrVersion, string currentVersion)
    {
        string cleanLatest = CleanVersionString(latestTagOrVersion);
        string cleanCurrent = CleanVersionString(currentVersion);

        if (Version.TryParse(cleanLatest, out Version? vLatest) && Version.TryParse(cleanCurrent, out Version? vCurrent))
        {
            return vLatest > vCurrent;
        }

        // 回退到分段整数对比
        string[] latestParts = cleanLatest.Split('-', '+')[0].Split('.');
        string[] currentParts = cleanCurrent.Split('-', '+')[0].Split('.');
        int maxLen = Math.Max(latestParts.Length, currentParts.Length);

        for (int i = 0; i < maxLen; i++)
        {
            int l = (i < latestParts.Length && int.TryParse(latestParts[i], out int lVal)) ? lVal : 0;
            int c = (i < currentParts.Length && int.TryParse(currentParts[i], out int cVal)) ? cVal : 0;
            if (l != c) return l > c;
        }

        return false;
    }

    public static string CleanVersionString(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "0.0.0";
        string trimmed = version.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed.Substring(1);
        }
        return trimmed;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion, bool force = false, CancellationToken ct = default)
    {
        if (!force && lastCheckResult != null)
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - lastCheckAt;
            if (elapsed < TimeSpan.FromMinutes(5) &&
                (lastCheckResult.Status == UpdateCheckStatus.UpToDate ||
                 lastCheckResult.Status == UpdateCheckStatus.UpdateAvailable ||
                 lastCheckResult.Status == UpdateCheckStatus.UpdateAvailableButManualOnly))
            {
                return lastCheckResult;
            }
        }

        await checkGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!force && lastCheckResult != null)
            {
                TimeSpan elapsed = DateTimeOffset.UtcNow - lastCheckAt;
                if (elapsed < TimeSpan.FromMinutes(5) &&
                    (lastCheckResult.Status == UpdateCheckStatus.UpToDate ||
                     lastCheckResult.Status == UpdateCheckStatus.UpdateAvailable ||
                     lastCheckResult.Status == UpdateCheckStatus.UpdateAvailableButManualOnly))
                {
                    return lastCheckResult;
                }
            }

            log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "UPDATE_CHECK_START", Message: $"currentVersion={currentVersion}, force={force}"));

            string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("OwHelper-Desktop", CleanVersionString(currentVersion)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "UPDATE_CHECK_FAILED", Message: $"Network error: {ex.Message}"));
                return new UpdateCheckResult(
                    UpdateCheckStatus.NetworkError,
                    null,
                    "检查更新失败：无法连接更新服务器。\n当前版本状态未知，请稍后重试。");
            }

            using (response)
            {
                int statusCode = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    bool isRateLimit = false;
                    if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values))
                    {
                        foreach (var v in values)
                        {
                            if (v == "0") { isRateLimit = true; break; }
                        }
                    }

                    UpdateCheckStatus st = isRateLimit ? UpdateCheckStatus.RateLimited : UpdateCheckStatus.ApiError;
                    string msg = isRateLimit
                        ? "GitHub 更新接口暂时达到访问限制。\n当前版本状态未知，请稍后重试。"
                        : "GitHub 更新服务暂时异常。\n当前版本状态未知。";

                    log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "UPDATE_CHECK_FAILED", Message: $"HTTP {statusCode} (rateLimit={isRateLimit})"));
                    return new UpdateCheckResult(st, null, msg, statusCode);
                }

                if ((int)response.StatusCode == 429)
                {
                    log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "UPDATE_CHECK_FAILED", Message: "HTTP 429 Too Many Requests"));
                    return new UpdateCheckResult(
                        UpdateCheckStatus.RateLimited,
                        null,
                        "GitHub 更新接口暂时达到访问限制。\n当前版本状态未知，请稍后重试。",
                        429);
                }

                if ((int)response.StatusCode >= 500)
                {
                    log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "UPDATE_CHECK_FAILED", Message: $"HTTP {statusCode} Server Error"));
                    return new UpdateCheckResult(
                        UpdateCheckStatus.ApiError,
                        null,
                        "GitHub 更新服务暂时异常。\n当前版本状态未知。",
                        statusCode);
                }

                if (!response.IsSuccessStatusCode)
                {
                    log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "UPDATE_CHECK_FAILED", Message: $"HTTP {statusCode} Unexpected"));
                    return new UpdateCheckResult(
                        UpdateCheckStatus.ApiError,
                        null,
                        "GitHub 更新服务暂时异常。\n当前版本状态未知。",
                        statusCode);
                }

                string json;
                try
                {
                    json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "UPDATE_CHECK_FAILED", Message: $"Content read error: {ex.Message}"));
                    return new UpdateCheckResult(
                        UpdateCheckStatus.NetworkError,
                        null,
                        "检查更新失败：无法连接更新服务器。\n当前版本状态未知，请稍后重试。");
                }

                UpdateCheckResult checkResult;
                try
                {
                    checkResult = ParseReleaseJson(json, currentVersion);
                }
                catch (Exception ex)
                {
                    log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "UPDATE_CHECK_FAILED", Message: $"Invalid response format: {ex.Message}"));
                    return new UpdateCheckResult(
                        UpdateCheckStatus.InvalidResponse,
                        null,
                        "更新信息格式异常，已停止自动更新。\n为安全起见，本程序不会下载或执行未知安装包。");
                }

                if (checkResult.Status == UpdateCheckStatus.UpToDate)
                {
                    log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "UPDATE_CHECK_UP_TO_DATE", Message: checkResult.Message));
                }
                else if (checkResult.Status == UpdateCheckStatus.UpdateAvailable || checkResult.Status == UpdateCheckStatus.UpdateAvailableButManualOnly)
                {
                    log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "UPDATE_CHECK_AVAILABLE", Message: $"Version={checkResult.Update?.Version}, ManualOnly={checkResult.Status == UpdateCheckStatus.UpdateAvailableButManualOnly}"));
                }

                lastCheckResult = checkResult;
                lastCheckAt = DateTimeOffset.UtcNow;
                return checkResult;
            }
        }
        finally
        {
            checkGate.Release();
        }
    }

    public static UpdateCheckResult ParseReleaseJson(string json, string currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        bool isDraft = root.TryGetProperty("draft", out JsonElement draftElem) && draftElem.GetBoolean();
        bool isPrerelease = root.TryGetProperty("prerelease", out JsonElement prereleaseElem) && prereleaseElem.GetBoolean();
        if (isDraft || isPrerelease)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.UpToDate,
                null,
                $"当前版本 v{CleanVersionString(currentVersion)} 已是最新版本。");
        }

        string tagName = root.TryGetProperty("tag_name", out JsonElement tagElem) ? (tagElem.GetString() ?? "") : "";
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.InvalidResponse,
                null,
                "更新信息格式异常，已停止自动更新。\n为安全起见，本程序不会下载或执行未知安装包。");
        }

        string title = root.TryGetProperty("name", out JsonElement nameElem) ? (nameElem.GetString() ?? "") : tagName;
        string body = root.TryGetProperty("body", out JsonElement bodyElem) ? (bodyElem.GetString() ?? "") : "";
        string htmlUrl = root.TryGetProperty("html_url", out JsonElement htmlElem) ? (htmlElem.GetString() ?? "") : "";
        DateTimeOffset? publishedAt = root.TryGetProperty("published_at", out JsonElement pubElem) && pubElem.TryGetDateTimeOffset(out DateTimeOffset dt) ? dt : null;

        string cleanVersion = CleanVersionString(tagName);
        if (!IsNewerVersion(cleanVersion, currentVersion))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.UpToDate,
                null,
                $"当前版本 v{CleanVersionString(currentVersion)} 已是最新版本。");
        }

        string setupUrl = "";
        string setupSha256 = "";
        long setupSize = 0;
        long setupAssetId = 0;
        string zipUrl = "";

        if (root.TryGetProperty("assets", out JsonElement assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assetsElem.EnumerateArray())
            {
                string assetName = asset.TryGetProperty("name", out JsonElement aName) ? (aName.GetString() ?? "") : "";
                string downloadUrl = asset.TryGetProperty("browser_download_url", out JsonElement aUrl) ? (aUrl.GetString() ?? "") : "";
                long size = asset.TryGetProperty("size", out JsonElement aSize) ? aSize.GetInt64() : 0;
                long assetId = asset.TryGetProperty("id", out JsonElement aId) ? aId.GetInt64() : 0;
                string digest = asset.TryGetProperty("digest", out JsonElement aDig) ? (aDig.GetString() ?? "") : "";

                if (assetName.StartsWith("OwHelper-Setup", StringComparison.OrdinalIgnoreCase) &&
                    assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    setupUrl = downloadUrl;
                    setupSize = size;
                    setupAssetId = assetId;

                    Match m = Sha256DigestRegex.Match(digest);
                    if (m.Success)
                    {
                        setupSha256 = m.Groups[1].Value.ToLowerInvariant();
                    }
                }
                else if (assetName.StartsWith("OwHelper-win-x64", StringComparison.OrdinalIgnoreCase) &&
                         !assetName.Contains("framework-dependent", StringComparison.OrdinalIgnoreCase) &&
                         assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    zipUrl = downloadUrl;
                }
            }
        }

        bool hasValidSetup = !string.IsNullOrWhiteSpace(setupUrl)
            && setupSize > 0
            && setupAssetId > 0
            && !string.IsNullOrWhiteSpace(setupSha256);

        var info = new UpdateInfo(
            Version: cleanVersion,
            TagName: tagName,
            Title: string.IsNullOrWhiteSpace(title) ? tagName : title,
            ReleaseNotes: body,
            SetupDownloadUrl: setupUrl,
            SetupSha256: setupSha256,
            SetupSizeBytes: setupSize,
            SetupAssetId: setupAssetId,
            ZipDownloadUrl: zipUrl,
            PublishedAt: publishedAt,
            HtmlUrl: htmlUrl);

        if (hasValidSetup)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                info,
                $"发现新版本 v{cleanVersion}。");
        }
        else
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailableButManualOnly,
                info,
                $"发现新版本 v{cleanVersion}，但该版本缺少可验证的自动安装包。\n请通过 GitHub Releases 手动下载。");
        }
    }

    public async Task<VerifiedUpdatePackage> DownloadAndVerifyUpdateAsync(
        UpdateInfo info,
        string destinationPath,
        IProgress<DownloadProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(info.SetupDownloadUrl))
        {
            log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "UPDATE_INSTALL_ABORTED", Message: $"version={info.Version}, reason=Missing setup download URL"));
            throw new InvalidOperationException("未找到最新安装包下载地址。");
        }

        if (!Uri.TryCreate(info.SetupDownloadUrl, UriKind.Absolute, out var setupUri) ||
            setupUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(setupUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "UPDATE_INSTALL_ABORTED", Message: $"version={info.Version}, reason=Untrusted setup URL host or scheme: {info.SetupDownloadUrl}"));
            throw new InvalidOperationException("安装包下载地址不受信任，必须来自 https://github.com。");
        }

        if (string.IsNullOrWhiteSpace(info.SetupSha256) || !Sha256DigestRegex.IsMatch($"sha256:{info.SetupSha256}"))
        {
            log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "UPDATE_INSTALL_ABORTED", Message: $"version={info.Version}, reason=Invalid or missing SHA-256 digest: {info.SetupSha256}"));
            throw new InvalidOperationException("安装包缺少有效的 SHA-256 校验摘要，已停止安装。");
        }

        if (info.SetupSizeBytes <= 0)
        {
            log?.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, "UPDATE_INSTALL_ABORTED", Message: $"version={info.Version}, reason=Invalid expected setup size: {info.SetupSizeBytes}"));
            throw new InvalidOperationException("安装包大小信息无效，已停止安装。");
        }

        string? dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string tempPath = destinationPath + ".downloading";

        // 三源下载候选（主源 + 双通道容灾）
        var urlsToTry = new List<string>
        {
            info.SetupDownloadUrl,
            "https://ghproxy.net/" + info.SetupDownloadUrl,
            "https://mirror.ghproxy.com/" + info.SetupDownloadUrl,
        };

        Exception? lastException = null;

        foreach (string downloadUrl in urlsToTry)
        {
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var candidateUri) ||
                candidateUri.Scheme != Uri.UriSchemeHttps ||
                !AllowedHosts.Contains(candidateUri.Host))
            {
                continue;
            }

            string sourceHost = candidateUri.Host;
            TryDeleteFile(tempPath);
            ct.ThrowIfCancellationRequested();

            log?.Write(new LogEntry(
                DateTimeOffset.Now,
                LogLevel.Information,
                "UPDATE_DOWNLOAD_START",
                Message: $"version={info.Version}, sourceHost={sourceHost}"));

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? info.SetupSizeBytes;
                long bytesReceived = 0;

                await using (Stream contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (FileStream fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                {
                    byte[] buffer = new byte[65536];
                    int read;
                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                        bytesReceived += read;
                        if (totalBytes > 0)
                        {
                            double pct = Math.Min(100.0, (double)bytesReceived / totalBytes * 100.0);
                            progress?.Report(new DownloadProgressReport(bytesReceived, totalBytes, pct));
                        }
                    }
                }

                log?.Write(new LogEntry(
                    DateTimeOffset.Now,
                    LogLevel.Information,
                    "UPDATE_DOWNLOAD_COMPLETE",
                    Message: $"version={info.Version}, sourceHost={sourceHost}, bytesReceived={bytesReceived}"));

                // 强制验证顺序：
                // 1. 关闭 FileStream（退出 await using 已完成）
                // 2. FileInfo.Length == info.SetupSizeBytes
                var fi = new FileInfo(tempPath);
                if (!fi.Exists)
                {
                    throw new FileNotFoundException("下载临时文件不存在。", tempPath);
                }

                long actualSize = fi.Length;
                if (actualSize != info.SetupSizeBytes)
                {
                    log?.Write(new LogEntry(
                        DateTimeOffset.Now,
                        LogLevel.Warning,
                        "UPDATE_VERIFY_SIZE_MISMATCH",
                        Message: $"version={info.Version}, sourceHost={sourceHost}, expectedSize={info.SetupSizeBytes}, actualSize={actualSize}"));
                    TryDeleteFile(tempPath);
                    throw new InvalidDataException($"文件大小不匹配: 期望 {info.SetupSizeBytes} 字节，实际 {actualSize} 字节");
                }

                // 3. 计算 SHA-256
                string actualSha256;
                await using (FileStream hashStream = File.OpenRead(tempPath))
                {
                    byte[] hashBytes = await System.Security.Cryptography.SHA256.HashDataAsync(hashStream, ct).ConfigureAwait(false);
                    actualSha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }

                // 4. 与 SetupSha256 进行 OrdinalIgnoreCase 比较
                if (!string.Equals(actualSha256, info.SetupSha256, StringComparison.OrdinalIgnoreCase))
                {
                    log?.Write(new LogEntry(
                        DateTimeOffset.Now,
                        LogLevel.Warning,
                        "UPDATE_VERIFY_HASH_MISMATCH",
                        Message: $"version={info.Version}, sourceHost={sourceHost}, expectedSize={info.SetupSizeBytes}, actualSize={actualSize}, expectedSha256={info.SetupSha256}, actualSha256={actualSha256}"));
                    TryDeleteFile(tempPath);
                    throw new InvalidDataException($"文件哈希不匹配: 期望 {info.SetupSha256}，实际 {actualSha256}");
                }

                // 5. hash 完全匹配后才 rename 为 final
                log?.Write(new LogEntry(
                    DateTimeOffset.Now,
                    LogLevel.Information,
                    "UPDATE_VERIFY_OK",
                    Message: $"version={info.Version}, sourceHost={sourceHost}, size={actualSize}, sha256={actualSha256}"));

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
                File.Move(tempPath, destinationPath, overwrite: true);

                // 6. 返回 VerifiedUpdatePackage
                return new VerifiedUpdatePackage(
                    FilePath: destinationPath,
                    SizeBytes: actualSize,
                    Sha256: actualSha256,
                    SourceUrl: downloadUrl,
                    Verified: true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                TryDeleteFile(tempPath);
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                TryDeleteFile(tempPath);
                log?.Write(new LogEntry(
                    DateTimeOffset.Now,
                    LogLevel.Warning,
                    "UPDATE_DOWNLOAD_SOURCE_FAILED",
                    Message: $"version={info.Version}, sourceHost={sourceHost}, reason={ex.Message}"));
            }
        }

        string failureReason = lastException?.Message ?? "所有更新下载节点均不可用或验证失败。";
        log?.Write(new LogEntry(
            DateTimeOffset.Now,
            LogLevel.Warning,
            "UPDATE_INSTALL_ABORTED",
            Message: $"version={info.Version}, reason={failureReason}"));

        throw new InvalidOperationException($"下载或校验新版本安装包失败: {failureReason}", lastException);
    }

    public async Task DownloadUpdateAsync(
        UpdateInfo info,
        string destinationPath,
        IProgress<DownloadProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        await DownloadAndVerifyUpdateAsync(info, destinationPath, progress, ct).ConfigureAwait(false);
    }

    static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 忽略临时文件清理失败
        }
    }

    public static string CreateRestartScript(string installerPath, string? targetExePath = null)
    {
        targetExePath ??= StandardExecutablePath;
        string scriptPath = Path.Combine(Path.GetTempPath(), "owhelper_update_restart.cmd");

        string scriptContent = $@"@echo off
timeout /t 1 /nobreak >nul
""{installerPath}"" /SILENT /SUPPRESSMSGBOXES
timeout /t 1 /nobreak >nul
start """" ""{targetExePath}""
del ""%~f0""
";

        File.WriteAllText(scriptPath, scriptContent);
        return scriptPath;
    }

    public static void ExecuteInstallerAndExit(string installerPath, bool silent = true, Action? onBeforeExit = null)
    {
        if (silent)
        {
            string scriptPath = CreateRestartScript(installerPath);
            var psi = new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            Process.Start(psi);
        }
        else
        {
            var psi = new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
            };
            Process.Start(psi);
        }

        onBeforeExit?.Invoke();
        Environment.Exit(0);
    }
}
