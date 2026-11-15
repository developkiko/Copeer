using System.Text;
using System.Text.Json;

namespace Copeer.Core;

public record GifItem(string PreviewUrl, string FullUrl);

/// <summary>Страница выдачи: элементы + курсор для следующей страницы (null — выдача кончилась).</summary>
public record GifPage(List<GifItem> Items, string? Next);

/// <summary>
/// Поиск GIF без своего API-ключа: используется ключ веб-клиента Tenor (тот же
/// tenor.googleapis.com/v2, что дёргает tenor.com из браузера). Ключ открыт —
/// лежит в HTML самого сайта, регистрация не нужна; лимиты общие с сайтом.
/// </summary>
public static class GifSearch
{
    // ключ веб-клиента tenor.com (script id="data" → base64 → API_V2_KEY)
    const string Key = "AIzaSyCZt6SSh5VgVPzD9fhyzG1DprdPRhtoaR4";

    // размер страницы выдачи: 4 колонки × 4 ряда — как раз вмещается в панель без прокрутки
    public const int PageSize = 16;

    static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Copeer/0.4");
        return h;
    }

    /// <summary>Поиск по запросу; pos — курсор следующей страницы из прошлого ответа.</summary>
    public static Task<GifPage> SearchAsync(string query, string? pos = null, CancellationToken ct = default)
    {
        var url = "https://tenor.googleapis.com/v2/search" +
                  $"?key={Key}&client_key=tenor_web" +
                  $"&q={Uri.EscapeDataString(query.Trim())}" +
                  $"&media_filter=gif,tinygif&contentfilter=off&limit={PageSize}";
        if (pos != null) url += $"&pos={Uri.EscapeDataString(pos)}";
        return FetchAsync(url, ct);
    }

    /// <summary>Топ (featured) — стартовое содержимое вкладки GIF, без запроса.</summary>
    public static Task<GifPage> FeaturedAsync(string? pos = null, CancellationToken ct = default)
    {
        var url = "https://tenor.googleapis.com/v2/featured" +
                  $"?key={Key}&client_key=tenor_web" +
                  $"&media_filter=gif,tinygif&contentfilter=off&limit={PageSize}";
        if (pos != null) url += $"&pos={Uri.EscapeDataString(pos)}";
        return FetchAsync(url, ct);
    }

    static async Task<GifPage> FetchAsync(string url, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsStringAsync(ct);

        var items = new List<GifItem>();
        using var doc = JsonDocument.Parse(raw);
        foreach (var r in doc.RootElement.GetProperty("results").EnumerateArray())
        {
            if (!r.TryGetProperty("media_formats", out var mf)) continue;
            string? full = mf.TryGetProperty("gif", out var g) ? g.GetProperty("url").GetString() : null;
            string? prev = mf.TryGetProperty("tinygif", out var t) ? t.GetProperty("url").GetString() : null;
            if (full != null && prev != null) items.Add(new GifItem(prev, full));
        }
        var next = doc.RootElement.TryGetProperty("next", out var n) ? n.GetString() : null;
        return new GifPage(items, next);
    }

    /// <summary>Байты медиа по URL (превью для сетки).</summary>
    public static async Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
        => await Http.GetByteArrayAsync(url, ct);

    static string CacheDir => Path.Combine(Settings.AppDir, "gifcache");

    /// <summary>Полный gif для вставки: качается в кеш и переиспользуется между вставками.</summary>
    public static async Task<string?> DownloadAsync(GifItem item, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var file = Path.Combine(CacheDir, Hash(item.FullUrl) + ".gif");
            if (!File.Exists(file))
            {
                var bytes = await Http.GetByteArrayAsync(item.FullUrl, ct);
                await File.WriteAllBytesAsync(file, bytes);
            }
            return file;
        }
        catch (Exception e)
        {
            Log.Error("gif download", e);
            return null;
        }
    }

    static string Hash(string url) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..20].ToLowerInvariant();
}
