using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Copeer.Core;

public class NeuroooException : Exception
{
    public HttpStatusCode Status { get; }
    public NeuroooException(HttpStatusCode status, string message) : base(message) => Status = status;
}

/// <summary>
/// Клиент официального API Neurooo v1 (https://neurooo.com/en/docs/api).
/// Ключ передаётся в теле запроса (api_key), как разрешено документацией.
/// </summary>
public class NeuroooClient
{
    static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var h = new HttpClient
        {
            BaseAddress = new Uri("https://neurooo.com/api/v1/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Copeer/0.1 (windows desktop)");
        return h;
    }

    public async Task<(string Target, string SourceLang)> TranslateAsync(
        Settings s, string text, string targetLang,
        string? sourceLang = null, string? engine = null, string? tone = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["source"] = text,
            ["target_language_code"] = targetLang
        };
        if (!string.IsNullOrEmpty(sourceLang) && sourceLang != "auto")
            body["source_language_code"] = sourceLang;
        if (!string.IsNullOrEmpty(engine))
            body["engine"] = engine;
        if (tone is "formal" or "informal")
            body["tone"] = tone;
        if (!string.IsNullOrWhiteSpace(s.NeuroooApiKey))
            body["api_key"] = s.NeuroooApiKey;

        using var resp = await Http.PostAsJsonAsync("translate.json", body);
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new NeuroooException(resp.StatusCode, DescribeError(resp.StatusCode, raw));

        using var doc = JsonDocument.Parse(raw);
        var target = doc.RootElement.GetProperty("target").GetString() ?? "";
        var src = doc.RootElement.TryGetProperty("source_language_code", out var sl) ? sl.GetString() ?? "" : "";
        return (target, src);
    }

    public async Task<(string Code, string Name)> DetectAsync(Settings s, string text)
    {
        var body = new Dictionary<string, object?> { ["source"] = text };
        if (!string.IsNullOrWhiteSpace(s.NeuroooApiKey))
            body["api_key"] = s.NeuroooApiKey;

        using var resp = await Http.PostAsJsonAsync("detect_language.json", body);
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new NeuroooException(resp.StatusCode, DescribeError(resp.StatusCode, raw));

        using var doc = JsonDocument.Parse(raw);
        return (doc.RootElement.GetProperty("code").GetString() ?? "",
                doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "");
    }

    public async Task<List<(string Id, string Name)>> EnginesAsync(Settings s)
    {
        using var resp = await Http.GetAsync("engines.json");
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new NeuroooException(resp.StatusCode, DescribeError(resp.StatusCode, raw));

        var result = new List<(string, string)>();
        using var doc = JsonDocument.Parse(raw);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var id = el.TryGetProperty("id", out var i) ? i.GetString() : null;
            var name = el.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (id != null && name != null) result.Add((id, name));
        }
        return result;
    }

    static string DescribeError(HttpStatusCode st, string raw)
    {
        string detail = "";
        try
        {
            using var d = JsonDocument.Parse(raw);
            if (d.RootElement.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array)
                detail = string.Join("; ", e.EnumerateArray().Select(x => x.GetString()));
        }
        catch { }
        if (st == HttpStatusCode.Unauthorized)
            return "Нет доступа: нужен API-ключ Neurooo (neurooo.com → API). " + detail;
        return $"Ошибка Neurooo API ({(int)st}): {detail}";
    }
}
