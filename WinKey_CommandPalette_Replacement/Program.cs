// Build as WinExe (.NET 6/7/8). Run as Administrator for coverage in elevated apps.
// Pure state-based handling; always blocks Win key and simulates the appropriate sequence
using System.Globalization; // add this at the top with your other using statements
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class Program
{
    static Shortcut _paletteShortcut;
    // ===== Virtual keys =====
    const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    const int VK_ESCAPE = 0x1B;
    const int VK_CONTROL = 0x11, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
    const int VK_MENU = 0x12;     // Alt
    const int VK_SHIFT = 0x10, VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;
    const int VK_SPACE = 0x20;
    const int VK_F12 = 0x7B; 

    // ===== Key event flags =====
    const uint KEYEVENTF_KEYUP = 0x0002;

    // ===== Hooks & messages =====
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const int WM_HOTKEY = 0x0312;
    const int LLKHF_INJECTED = 0x10;

    // Global hotkey mods
    const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    const int HOTKEY_ID_QUIT = 1;

    static IntPtr _hook = IntPtr.Zero;
    static LowLevelKeyboardProc _proc = HookProc;

    // State tracking
    static bool _winDown = false;
    static bool _ctrlDown = false;
    static bool _shiftDown = false;
    static bool _passthroughMode = false;  // NEW: Passthrough mode flag
    static int _winKeyPressed = 0;
    static List<KeyEvent> _keysWhileWinDown = new List<KeyEvent>();

    // Tag injected inputs so our hook ignores them
    static readonly IntPtr OUR_TAG = new IntPtr(unchecked((int)0xB00BF00D));

    struct KeyEvent
    {
        public int VirtualKey;
        public bool IsDown;
        public IntPtr WParam;
    }

    [STAThread]
    static void Main()
    {
        Console.WriteLine("Starting Win key remapper with debugging...");

        // Test basic input capabilities at startup
        Console.WriteLine("Testing basic input capabilities...");
        TestInputCapabilities();

        _paletteShortcut = LoadShortcutConfigOrDefault();

        Console.WriteLine("Starting Win key remapper with debugging...");
        Console.WriteLine($"Using shortcut: Win={_paletteShortcut.Win}, Ctrl={_paletteShortcut.Ctrl}, Alt={_paletteShortcut.Alt}, Shift={_paletteShortcut.Shift}, MainKey=0x{_paletteShortcut.MainKey:X}");

        // Install low-level keyboard hook
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        if (_hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();

        // Register a global panic hotkey: Ctrl+Alt+F12
        if (!RegisterHotKey(IntPtr.Zero, HOTKEY_ID_QUIT, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)VK_F12))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        Application.AddMessageFilter(new HotkeyFilter(msgId: HOTKEY_ID_QUIT, onHit: Quit));
        Application.ApplicationExit += (_, __) => Cleanup();

        Console.WriteLine("Hook installed. Press Ctrl+Alt+F12 to quit.");
        Application.Run();
    }
    static Shortcut LoadShortcutConfigOrDefault()
    {
        try
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(exeDir, "shortcut.json");

            if (!File.Exists(configPath))
            {
                Console.WriteLine("shortcut.json not found, using default Win+Alt+Space");
                return new Shortcut
                {
                    Win = true,
                    Ctrl = false,
                    Alt = true,
                    Shift = false,
                    MainKey = VK_SPACE
                };
            }

            string json = File.ReadAllText(configPath);
            var cfg = JsonSerializer.Deserialize<ShortcutConfig>(json);

            if (cfg == null)
                throw new Exception("shortcut.json deserialized to null");

            return new Shortcut
            {
                Win = cfg.Win,
                Ctrl = cfg.Ctrl,
                Alt = cfg.Alt,
                Shift = cfg.Shift,
                MainKey = MapKeyNameToVk(cfg.MainKey)
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load shortcut.json: {ex.Message}");
            Console.WriteLine("Falling back to default Win+Alt+Space");

            return new Shortcut
            {
                Win = true,
                Ctrl = false,
                Alt = true,
                Shift = false,
                MainKey = VK_SPACE
            };
        }
    } 
    static int MapKeyNameToVk(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return VK_SPACE;

        // Accept things like "Ctrl+Shift+P" but only use the last token as the main key,
        // since modifiers are already handled by the Win/Ctrl/Alt/Shift booleans in JSON
        var raw = name.Trim();
        var parts = raw.Split('+', StringSplitOptions.RemoveEmptyEntries);
        var s = parts[^1].Trim().ToUpperInvariant();

        // 1) Single character: letters A–Z or digits 0–9
        if (s.Length == 1)
        {
            char c = s[0];
            if (c >= 'A' && c <= 'Z')
                return c;          // 'A' → 0x41, etc.
            if (c >= '0' && c <= '9')
                return c;          // '0' → 0x30, etc.
        }

        // 2) Function keys: F1–F24
        if (s.Length >= 2 && s[0] == 'F' && int.TryParse(s.Substring(1), out int fn))
        {
            if (fn >= 1 && fn <= 24)
                return 0x70 + (fn - 1); // F1=0x70, F2=0x71, ...
        }

        // 3) Raw hex virtual key code: e.g. "0x5B"
        if (s.StartsWith("0X") && int.TryParse(s.Substring(2), NumberStyles.HexNumber,
                                                CultureInfo.InvariantCulture, out int hexVk))
        {
            return hexVk;
        }

        // 4) Named special keys
        switch (s)
        {
            case "SPACE":
            case "SPACEBAR":
                return VK_SPACE;

            case "ESC":
            case "ESCAPE":
                return VK_ESCAPE;

            case "TAB":
                return 0x09;

            case "ENTER":
            case "RETURN":
                return 0x0D;

            case "BACKSPACE":
            case "BACK":
                return 0x08;

            case "INSERT":
            case "INS":
                return 0x2D;

            case "DELETE":
            case "DEL":
                return 0x2E;

            case "HOME":
                return 0x24;

            case "END":
                return 0x23;

            case "PAGEUP":
            case "PGUP":
                return 0x21;

            case "PAGEDOWN":
            case "PGDN":
                return 0x22;

            case "UP":
            case "UPARROW":
                return 0x26;

            case "DOWN":
            case "DOWNARROW":
                return 0x28;

            case "LEFT":
            case "LEFTARROW":
                return 0x25;

            case "RIGHT":
            case "RIGHTARROW":
                return 0x27;

            case "CAPSLOCK":
            case "CAPS":
                return 0x14;

            // You can add more here: WIN, MENU, OEM keys, media keys, etc.
            default:
                Console.WriteLine($"Unknown key name '{name}', defaulting to SPACE");
                return VK_SPACE;
        }
    }

    static void TestInputCapabilities()
    {
        Console.WriteLine("=== Input Capability Test ===");

        // Test 1: Simple SendInput
        var testInput = new INPUT[] { KeyDown(VK_SPACE), KeyUp(VK_SPACE) };
        SetLastError(0);
        uint sendResult = SendInput((uint)testInput.Length, testInput, Marshal.SizeOf(typeof(INPUT)));
        int sendError = Marshal.GetLastWin32Error();
        Console.WriteLine($"SendInput test: {sendResult}/{testInput.Length}, error: {sendError}");

        // Test 2: keybd_event
        try
        {
            SetLastError(0);
            keybd_event((byte)VK_SPACE, 0, 0, IntPtr.Zero);  // Space down
            keybd_event((byte)VK_SPACE, 0, KEYEVENTF_KEYUP, IntPtr.Zero);  // Space up
            int keybdError = Marshal.GetLastWin32Error();
            Console.WriteLine($"keybd_event test: completed, error: {keybdError}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"keybd_event test: failed with exception: {ex.Message}");
        }

        Console.WriteLine("=== End Test ===\n");
    }

    static void Quit()
    {
        Console.WriteLine("Quitting...");
        Cleanup();
        Application.ExitThread();
        Environment.Exit(0);
    }

    static void Cleanup()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        UnregisterHotKey(IntPtr.Zero, HOTKEY_ID_QUIT);
    }

    static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Ignore our own injected inputs - be more strict about this
            bool injected = (info.flags & LLKHF_INJECTED) != 0;
            bool ourTag = info.dwExtraInfo == OUR_TAG;

            if (injected || ourTag)
            {
                Console.WriteLine($"Ignoring injected event: {info.vkCode:X} injected={injected} ourTag={ourTag}");
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            bool isDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            bool isUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;
            int vk = info.vkCode;

            // Debug output
            Console.WriteLine($"Key: {vk:X} ({(VirtualKeyCode)vk}) {(isDown ? "DOWN" : "UP")} - Win: {_winDown}, Passthrough: {_passthroughMode}");

            // Track Shift state
            if (vk == VK_LSHIFT || vk == VK_RSHIFT || vk == VK_SHIFT)
            {
                _shiftDown = isDown;
            }

            // Track Ctrl state
            if (vk == VK_LCONTROL || vk == VK_RCONTROL || vk == VK_CONTROL)
            {
                if (isDown) _ctrlDown = true;
                if (isUp) _ctrlDown = false;

                // If Win is down, handle based on passthrough mode
                if (_winDown)
                {
                    if (!_passthroughMode && isDown) // First key pressed after Win
                    {
                        Console.WriteLine($"Second key detected: {vk:X} - entering passthrough mode");

                        // Send Win key down to Windows immediately
                        keybd_event((byte)_winKeyPressed, 0, 0, OUR_TAG);
                        _passthroughMode = true;

                        // Let this key (and all future keys) pass through naturally
                        return CallNextHookEx(_hook, nCode, wParam, lParam);
                    }
                    else if (_passthroughMode)
                    {
                        // In passthrough mode - let everything pass through naturally
                        Console.WriteLine($"Passthrough mode: {vk:X} {(isDown ? "DOWN" : "UP")}");
                        return CallNextHookEx(_hook, nCode, wParam, lParam);
                    }
                    else
                    {
                        // Still monitoring for first key (or key up events before passthrough)
                        _keysWhileWinDown.Add(new KeyEvent { VirtualKey = vk, IsDown = isDown, WParam = wParam });
                        return (IntPtr)1; // Block it
                    }
                }

                return CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            // Handle Ctrl+Esc
            if (vk == VK_ESCAPE && _ctrlDown && !_shiftDown)
            {
                if (isDown)
                {
                    Console.WriteLine("Ctrl+Esc detected - firing command palette");
                    ActivateCommandPalette();
                }
                return (IntPtr)1;
            }

            // Handle Windows keys
            if (vk == VK_LWIN || vk == VK_RWIN)
            {
                if (isDown)
                {
                    Console.WriteLine($"Win key down: {vk:X}");
                    _winDown = true;
                    _winKeyPressed = vk;
                    _keysWhileWinDown.Clear();
                    _passthroughMode = false; // Reset passthrough mode
                    return (IntPtr)1; // Always block Win key
                }
                if (isUp)
                {
                    Console.WriteLine($"Win key up: {vk:X}, passthrough mode: {_passthroughMode}");
                    _winDown = false; 

                    if (_passthroughMode)
                    {
                        Console.WriteLine("Ending passthrough mode - sending Win key up");
                        // Send Win key up to Windows to complete the sequence
                        keybd_event((byte)_winKeyPressed, 0, KEYEVENTF_KEYUP, OUR_TAG);
                        _passthroughMode = false;
                    }
                    else
                    {
                        // Solo Win tap
                        Console.WriteLine("Solo Win tap detected - firing command palette");
                        ActivateCommandPalette();
                    }

                    _keysWhileWinDown.Clear();
                    return (IntPtr)1;
                }
            }
            else
            {
                // Any other key while Win is held
                if (_winDown)
                {
                    if (!_passthroughMode && isDown) // First key pressed after Win
                    {
                        Console.WriteLine($"Second key detected: {vk:X} - entering passthrough mode");

                        // Send Win key down to Windows immediately
                        keybd_event((byte)_winKeyPressed, 0, 0, OUR_TAG);
                        _passthroughMode = true;

                        // Let this key (and all future keys) pass through naturally
                        return CallNextHookEx(_hook, nCode, wParam, lParam);
                    }
                    else if (_passthroughMode)
                    {
                        // In passthrough mode - let everything pass through naturally
                        Console.WriteLine($"Passthrough mode: {vk:X} {(isDown ? "DOWN" : "UP")}");
                        return CallNextHookEx(_hook, nCode, wParam, lParam);
                    }
                    else
                    {
                        // Still monitoring for first key (or key up events before passthrough)
                        _keysWhileWinDown.Add(new KeyEvent { VirtualKey = vk, IsDown = isDown, WParam = wParam });
                        return (IntPtr)1; // Block it
                    }
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    static void ActivateCommandPalette()
    {
        Console.WriteLine("Activating command palette shortcut...");

        if (!SendShortcutWithSendInput(_paletteShortcut))
        {
            Console.WriteLine("SendInput failed, falling back to keybd_event...");
            SendShortcutWithKeybdEvent(_paletteShortcut);
        }
    }

    static bool SendShortcutWithSendInput(Shortcut shortcut)
    {
        var inputs = new List<INPUT>();

        // Modifiers down
        if (shortcut.Win) inputs.Add(KeyDown(VK_LWIN));
        if (shortcut.Ctrl) inputs.Add(KeyDown(VK_CONTROL));
        if (shortcut.Alt) inputs.Add(KeyDown(VK_MENU));
        if (shortcut.Shift) inputs.Add(KeyDown(VK_SHIFT));

        // Main key down/up
        inputs.Add(KeyDown(shortcut.MainKey));
        inputs.Add(KeyUp(shortcut.MainKey));

        // Modifiers up
        if (shortcut.Shift) inputs.Add(KeyUp(VK_SHIFT));
        if (shortcut.Alt) inputs.Add(KeyUp(VK_MENU));
        if (shortcut.Ctrl) inputs.Add(KeyUp(VK_CONTROL));
        if (shortcut.Win) inputs.Add(KeyUp(VK_LWIN));

        var arr = inputs.ToArray();
        for (int i = 0; i < arr.Length; i++)
            arr[i].U.ki.dwExtraInfo = OUR_TAG;

        SetLastError(0);
        uint sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
        int err = Marshal.GetLastWin32Error();

        Console.WriteLine($"SendInput shortcut result: {sent}/{arr.Length}, error: {err}");
        return sent == arr.Length && sent > 0;
    }

    static void SendShortcutWithKeybdEvent(Shortcut shortcut)
    {
        Console.WriteLine("=== FALLBACK: Using keybd_event for shortcut ===");

        try
        {
            void Down(int vk) => keybd_event((byte)vk, 0, 0, OUR_TAG);
            void Up(int vk) => keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, OUR_TAG);

            if (shortcut.Win) Down(VK_LWIN);
            if (shortcut.Ctrl) Down(VK_CONTROL);
            if (shortcut.Alt) Down(VK_MENU);
            if (shortcut.Shift) Down(VK_SHIFT);
            System.Threading.Thread.Sleep(5);

            Down(shortcut.MainKey);
            System.Threading.Thread.Sleep(5);
            Up(shortcut.MainKey);
            System.Threading.Thread.Sleep(5);

            if (shortcut.Shift) Up(VK_SHIFT);
            if (shortcut.Alt) Up(VK_MENU);
            if (shortcut.Ctrl) Up(VK_CONTROL);
            if (shortcut.Win) Up(VK_LWIN);

            Console.WriteLine("keybd_event shortcut sequence sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"keybd_event shortcut failed: {ex.Message}");
        }
    }

    static INPUT KeyDown(int vk) => new INPUT
    {
        type = 1, // INPUT_KEYBOARD
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = 0,
                dwFlags = 0, // Key down
                time = 0,
                dwExtraInfo = OUR_TAG
            }
        }
    };

    static INPUT KeyUp(int vk) => new INPUT
    {
        type = 1, // INPUT_KEYBOARD
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = 0,
                dwFlags = 2u, // KEYEVENTF_KEYUP
                time = 0,
                dwExtraInfo = OUR_TAG
            }
        }
    };

    // Virtual key codes for debugging
    enum VirtualKeyCode
    {
        LWIN = 0x5B,
        RWIN = 0x5C,
        CONTROL = 0x11,
        MENU = 0x12,
        SPACE = 0x20,
        ESCAPE = 0x1B,
        R = 0x52,
        L = 0x4C,
        D = 0x44,
        // Add more as needed
    }

    sealed class HotkeyFilter : IMessageFilter
    {
        private readonly int _id;
        private readonly Action _onHit;
        public HotkeyFilter(int msgId, Action onHit) { _id = msgId; _onHit = onHit; }
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == _id)
            {
                _onHit();
                return true;
            }
            return false;
        }
    }

    // P/Invoke declarations
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public int type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }


    struct Shortcut
    {
        public bool Win;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;
        public int MainKey; // virtual keycode, e.g. VK_SPACE or 'P'
    }

    class ShortcutConfig
    {
        public bool Win { get; set; }
        public bool Ctrl { get; set; }
        public bool Alt { get; set; }
        public bool Shift { get; set; }
        public string MainKey { get; set; } = "Space";
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("kernel32.dll")]
    static extern void SetLastError(uint dwErrCode);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
}
