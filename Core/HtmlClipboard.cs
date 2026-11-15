using System.Net;
using System.Text;

namespace Copeer.Core;

/// <summary>
/// Сборка «HTML Format» (CF_HTML) для буфера обмена: заголовок с UTF-8 смещениями + фрагмент.
/// Нужен, чтобы при мультивставке картинки ехали вместе с текстом: приложения, понимающие
/// HTML (Word, браузеры, мессенджеры), вставят и картинки, остальные — только текст.
/// </summary>
public static class HtmlClipboard
{
    /// <summary>Обернуть HTML-фрагмент в полный CF_HTML.</summary>
    public static string Wrap(string fragment)
    {
        const string pre = "<html><body>\r\n<!--StartFragment-->";
        const string post = "<!--EndFragment-->\r\n</body>\r\n</html>";

        // ширина чисел фиксированная (D9), поэтому длины сходятся за одну итерацию;
        // вторая — контроль стабильности
        int startHtml = 0, endHtml = 0, startFragment = 0, endFragment = 0;
        string header = "";
        for (int i = 0; i < 2; i++)
        {
            header =
                "Version:0.9\r\n" +
                $"StartHTML:{startHtml:D9}\r\n" +
                $"EndHTML:{endHtml:D9}\r\n" +
                $"StartFragment:{startFragment:D9}\r\n" +
                $"EndFragment:{endFragment:D9}\r\n";
            int hLen = Encoding.UTF8.GetByteCount(header);
            startHtml = hLen;
            startFragment = hLen + Encoding.UTF8.GetByteCount(pre);
            endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
            endHtml = endFragment + Encoding.UTF8.GetByteCount(post);
        }
        return header + pre + fragment + post;
    }

    /// <summary>Текст в HTML: экранирование спецсимволов, переносы строк → &lt;br&gt;.</summary>
    public static string EscapeText(string s) =>
        WebUtility.HtmlEncode(s).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br>");

    /// <summary>file:///-URL локального файла (с экранированием пробелов и кириллицы).</summary>
    public static string FileUrl(string path) => new Uri(path).AbsoluteUri;
}
