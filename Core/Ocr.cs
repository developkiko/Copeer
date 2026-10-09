using System.Net.Http.Json;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Copeer.Core;

public record OcrResult(bool Ok, string Text, string Error)
{
    public static OcrResult Good(string text) => new(true, text, "");
    public static OcrResult Fail(string error) => new(false, "", error);
}

/// <summary>
/// Распознавание текста на скриншоте. Два движка:
/// - "windows" — встроенный Windows OCR (офлайн, без ключа; нужен языковой пакет);
/// - "gcv"     — Google Cloud Vision TEXT_DETECTION (ключ, бесплатный лимит ~1000 запросов/мес).
/// </summary>
public static class Ocr
{
    /// <summary>Движок настроен и кнопка «Распознать текст» должна появляться на плашке скриншота.</summary>
    public static bool IsConfigured(Settings s) => s.OcrEngine switch
    {
        "windows" => true,
        "gcv" => !string.IsNullOrWhiteSpace(s.OcrGcvKey),
        _ => false
    };

    public static async Task<OcrResult> RecognizeAsync(Settings s, Bitmap bmp)
    {
        try
        {
            return s.OcrEngine switch
            {
                "windows" => await RecognizeWindowsAsync(bmp),
                "gcv" => await RecognizeGcvAsync(s.OcrGcvKey.Trim(), bmp),
                _ => OcrResult.Fail("Движок распознавания не выбран")
            };
        }
        catch (Exception e)
        {
            Log.Error("ocr", e);
            return OcrResult.Fail(e.Message);
        }
    }

    // ——— встроенный Windows OCR ———

    static async Task<OcrResult> RecognizeWindowsAsync(Bitmap bmp)
    {
        return await Task.Run(async () =>
        {
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
                return OcrResult.Fail("Нет OCR-пакета языка (Параметры → Время и язык → Язык)");

            using var soft = await ToSoftwareBitmapAsync(bmp);
            var res = await engine.RecognizeAsync(soft);
            var text = res.Text?.Trim() ?? "";
            return text.Length == 0 ? OcrResult.Fail("Текст не найден") : OcrResult.Good(text);
        });
    }

    static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bmp)
    {
        var png = ToPngBytes(bmp);
        var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(png);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
        writer.Dispose();

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var soft = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        stream.Dispose();
        return soft;
    }

    // ——— Google Cloud Vision ———

    static async Task<OcrResult> RecognizeGcvAsync(string key, Bitmap bmp)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var body = new
        {
            requests = new[]
            {
                new
                {
                    image = new { content = Convert.ToBase64String(ToPngBytes(bmp)) },
                    features = new[] { new { type = "TEXT_DETECTION" } }
                }
            }
        };
        using var resp = await client.PostAsJsonAsync(
            $"https://vision.googleapis.com/v1/images:annotate?key={Uri.EscapeDataString(key)}", body);
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            string detail = "";
            try
            {
                using var d = JsonDocument.Parse(raw);
                detail = d.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "";
            }
            catch { }
            return resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? OcrResult.Fail("Ключ отклонён Google: " + detail)
                : OcrResult.Fail($"Ошибка Vision API ({(int)resp.StatusCode}): {detail}");
        }

        using var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement.GetProperty("responses")[0]
            .TryGetProperty("fullTextAnnotation", out var fta) ? fta.GetProperty("text").GetString() ?? "" : "";
        return text.Trim().Length == 0 ? OcrResult.Fail("Текст не найден") : OcrResult.Good(text.Trim());
    }

    static byte[] ToPngBytes(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }
}
