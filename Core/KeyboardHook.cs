using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Copeer.Core;

/// <summary>
/// Низкоуровневый клавиатурный хук (WH_KEYBOARD_LL): перехватывает комбинации,
/// зарегистрированные самой Windows (Win+. — эмодзи-панель, Win+Shift+S — ножницы,
/// Win+T — проводник), раньше системной обработки.
/// Тяжёлая обработка через Sync выносится из колбэка хука — в колбэке только
/// проверка и глотание клавиши, иначе Windows сочтёт хук зависшим.
/// </summary>
public class KeyboardHook : IDisposable
{
    public Action<int>? ComboHit; // аргумент — vkCode нажатой клавиши

    /// <summary>Куда откладывать обработку (скрытое окно с message loop).</summary>
    public ISynchronizeInvoke? Sync;

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100;
    const int WM_SYSKEYDOWN = 0x0104;
    const uint LLKHF_INJECTED = 0x10;

    const uint VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    const uint VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    const uint VK_SNAPSHOT = 0x2C; // PrintScreen
    public const uint VK_PERIOD = 0xBE, VK_T = 0x54, VK_S = 0x53, VK_V = 0x56;

    readonly LowLevelKeyboardProc _proc;
    readonly Action<int> _dispatch;
    IntPtr _hook;
    readonly HashSet<uint> _skip = new(); // vk, занятые нашим RegisterHotKey

    /// <summary>PrintScreen (с любыми модификаторами) глотается и открывает скриншот Copeer.</summary>
    public bool InterceptSnapshot;

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? lpModuleName);

    public KeyboardHook()
    {
        _dispatch = SafeDispatch;
        _proc = HookProc;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            Log.Error($"keyboard hook failed: Win32 error {Marshal.GetLastWin32Error()}");
        else
            Log.Info("keyboard hook installed");
    }

    /// <summary>Комбинация уже занята нашим RegisterHotKey — хук её не глотает.</summary>
    public void Skip(uint vk) => _skip.Add(vk);

    IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
            {
                var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                if (kbd.vkCode is VK_PERIOD or VK_T or VK_S or VK_V)
                {
                    bool win = Key(VK_LWIN) || Key(VK_RWIN);
                    if (win)
                        Log.Info($"hook vk=0x{kbd.vkCode:X2} win=True shift={Key(VK_SHIFT)} " +
                                 $"ctrl={Key(VK_CONTROL)} alt={Key(VK_MENU)} inj={(kbd.flags & LLKHF_INJECTED) != 0}");
                }

                // PrintScreen — без требования Win, с любыми модификаторами
                if (InterceptSnapshot && kbd.vkCode == VK_SNAPSHOT)
                {
                    int snap = (int)kbd.vkCode;
                    if (Sync != null)
                        Sync.BeginInvoke(_dispatch, new object?[] { snap });
                    else
                        _dispatch(snap);
                    return (IntPtr)1;
                }

                if (!_skip.Contains(kbd.vkCode) && MatchesShift(kbd.vkCode))
                {
                    int vk = (int)kbd.vkCode;
                    if (Sync != null)
                        Sync.BeginInvoke(_dispatch, new object?[] { vk }); // вне колбэка хука
                    else
                        _dispatch(vk);
                    return (IntPtr)1; // глотаем — до системы не доходит
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("hook proc", ex);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    void SafeDispatch(int vk)
    {
        try
        {
            Log.Info($"combo hit: 0x{vk:X2}");
            ComboHit?.Invoke(vk);
        }
        catch (Exception ex)
        {
            Log.Error("hook dispatch", ex);
        }
    }

    // базовые комбинации (без Shift) + отдельно Win+Shift+S
    static bool Matches(uint vk)
    {
        if (!Key(VK_LWIN) && !Key(VK_RWIN)) return false;
        if (Key(VK_CONTROL) || Key(VK_MENU) || Key(VK_SHIFT)) return false;
        return vk is VK_PERIOD or VK_T or VK_V;
    }

    static bool MatchesShift(uint vk)
    {
        if (Matches(vk)) return true;
        if (!Key(VK_LWIN) && !Key(VK_RWIN)) return false;
        if (Key(VK_CONTROL) || Key(VK_MENU) || !Key(VK_SHIFT)) return false;
        return vk == VK_S; // Win + Shift + S
    }

    static bool Key(uint vk) => (GetAsyncKeyState((int)vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
