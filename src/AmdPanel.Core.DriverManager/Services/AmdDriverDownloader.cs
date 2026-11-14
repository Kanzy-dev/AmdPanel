using System.Net;
using System.Net.Http.Headers;
using AmdPanel.Core.DriverManager.Exceptions;
using AmdPanel.Core.DriverManager.Models;

namespace AmdPanel.Core.DriverManager.Services;

/// <summary>
/// HTTP-загрузчик пакетов драйверов AMD с обходом 403 Forbidden.
/// </summary>
/// <remarks>
/// <para>
/// <b>Почему без заголовков падает с 403</b> — AMD-сервера (amd.com,
/// drivers.amd.com) фильтруют запросы без браузерных заголовков и отдают
/// HTTP 403 для дефолтных User-Agent'ов .NET HttpClient. Решение:
/// добавить <c>Referer: https://amd.com</c> и стандартный браузерный
/// User-Agent. Реализовано в <see cref="BuildHttpClient"/> (Requirement 2).
/// </para>
/// </remarks>
public sealed class AmdDriverDownloader : IDisposable
{
    /// <summary>
    /// Полная строка браузерного User-Agent для обхода 403 от amd.com.
    /// ВАЖНО: нельзя передавать её в конструктор new ProductInfoHeaderValue(name, value) -
    /// парсер ProductInfoHeaderValue ожидает ИМЯ/ВЕРСИЮ без комментариев в скобках,
    /// и полная строка вида "Mozilla/5.0 (Windows NT 10.0...) AppleWebKit/537.36 ..."
    /// вызовет FormatException при конструировании (stack trace падает в
    /// AmdDriverDownloader.cs:line 22 в ProductInfoHeaderValue..ctor).
    /// Вместо этого используем UserAgent.ParseAdd / TryParseAdd, который
    /// корректно разбирает UA на product tokens и comment-сегменты.
    /// </summary>
    private const string UserAgentString =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private readonly HttpClient _httpClient;
    private bool _disposed;

    public AmdDriverDownloader()
    {
        _httpClient = BuildHttpClient();
    }

    /// <summary> Создаёт настроенный HttpClient с Referer + UA. </summary>
    private static HttpClient BuildHttpClient()
    {
        // ServicePointManager настройки: TLS 1.2 обязателен для amd.com
        ServicePointManager.SecurityProtocol =
            SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer(),
        });

        // v1.0.1 fix: используем ParseAdd/TryParseAdd вместо конструктора ProductInfoHeaderValue
        // Конструктор new ProductInfoHeaderValue(name, version) не принимает UA с комментариями
        // в скобках и крашит с FormatException. UserAgent.ParseAdd корректно разбивает строкку
        // на product tokens: Mozilla/5.0 + (Windows NT...) + AppleWebKit/537.36 + ... + Safari/537.36
        if (!client.DefaultRequestHeaders.UserAgent.TryParseAdd(UserAgentString))
        {
            // Fallback на минимальный UA если парсер всё же не принял
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0");
        }

        // Referer обязателен - без него AMD отдаёт 403 на большинстве URL
        try { client.DefaultRequestHeaders.Referrer = new Uri("https://amd.com"); }
        catch (ArgumentException) { /* ignore - referer optional fallback */ }

        try
        {
            client.DefaultRequestHeaders.AcceptLanguage.Add(
                new StringWithQualityHeaderValue("en-US", 1.0));
        }
        catch (ArgumentException) { /* ignore */ }

        try
        {
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/html"));
        }
        catch (ArgumentException) { /* ignore */ }
        client.Timeout = TimeSpan.FromMinutes(15);
        return client;
    }

    /// <summary> Скачивает файл с прогрессом через streamed response. </summary>
    /// <param name="url"> Прямой URL .exe пакета. </param>
    /// <param name="destinationPath"> Локальный путь для сохранения. </param>
    /// <param name="progress"> Action(percent, bytesReceived, totalBytes). </param>
    /// <param name="ct"> CancellationToken. </param>
    public async Task DownloadAsync(Uri url, string destinationPath,
        Action<int, long, long>? progress = null, CancellationToken ct = default)
    {
        // HEAD-запрос для получения Content-Length (не все серверы его отдают)
        long totalBytes = 0;
        try
        {
            using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
            using var headResp = await _httpClient.SendAsync(headReq, ct);
            if (headResp.Content.Headers.ContentLength is long cl)
                totalBytes = cl;
        }
        catch { /* не критично - прогресс будет без процента */ }

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new DriverDownloadException(
                "AMD server returned 403 Forbidden. URL: " + url +
                ". Если ошибка повторяется - возможно, AMD сменила CDN.", HttpStatusCode.Forbidden);
        if (!response.IsSuccessStatusCode)
            throw new DriverDownloadException(
                $"Download failed: HTTP {(int)response.StatusCode} {response.StatusCode}",
                response.StatusCode);

        if (totalBytes == 0 && response.Content.Headers.ContentLength is long cl2)
            totalBytes = cl2;

        string dir = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create,
            FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        long bytesReceived = 0;
        int lastReportedPercent = -1;
        int read;
        while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            bytesReceived += read;
            if (progress is not null && totalBytes > 0)
            {
                int pct = (int)(bytesReceived * 100 / totalBytes);
                if (pct != lastReportedPercent)
                {
                    progress(pct, bytesReceived, totalBytes);
                    lastReportedPercent = pct;
                }
            }
            else if (progress is not null && (bytesReceived % (1024 * 1024) == 0))
            {
                progress(-1, bytesReceived, totalBytes);
            }
        }
        progress?.Invoke(100, bytesReceived, totalBytes);
    }

    /// <summary> HEAD-проверка доступности URL без скачивания. </summary>
    public async Task<bool> IsAccessibleAsync(Uri url, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await _httpClient.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
