namespace Copeer.Core;

/// <summary>Языки для перевода (код + название). Общий список для переводчика и настроек.</summary>
public static class Langs
{
    public static readonly (string Code, string Name)[] List =
    {
        ("auto","— автоопределение —"), ("ru","Русский"), ("en","Английский"), ("uk","Українська"),
        ("de","Deutsch"), ("fr","Français"), ("es","Español"), ("it","Italiano"), ("pt","Português"),
        ("nl","Nederlands"), ("pl","Polski"), ("cs","Čeština"), ("tr","Türkçe"), ("zh","中文"),
        ("ja","日本語"), ("ko","한국어"), ("ar","العربية"), ("sv","Svenska"), ("fi","Suomi"),
        ("da","Dansk"), ("no","Norsk"), ("el","Ελληνικά"), ("he","עברית"), ("hi","हिन्दी"),
        ("th","ไทย"), ("vi","Tiếng Việt"), ("hu","Magyar"), ("ro","Română"), ("bg","Български"),
        ("sr","Српски"), ("hr","Hrvatski"), ("id","Indonesia")
    };

    public static int IndexOf(string code) => Array.FindIndex(List, l => l.Code == code);
}
