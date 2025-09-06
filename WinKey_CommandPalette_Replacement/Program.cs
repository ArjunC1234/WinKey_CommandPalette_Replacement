// Build as WinExe (.NET 6/7/8). Run as Administrator for coverage in elevated apps.
// Pure state-based handling; always blocks Win key and simulates the appropriate sequence

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Program
{
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
        Console.WriteLine("Sending Win+Alt+Space...");

        // Check current thread's input capabilities
        Console.WriteLine("Checking input capabilities...");

        // Try different approaches
        Console.WriteLine("Method 1: SendInput");
        if (!TrySendInputMethod())
        {
            Console.WriteLine("Method 2: keybd_event");
            TryKeybdEventMethod();
        }
    }

    static bool TrySendInputMethod()
    {
        var testInput = new INPUT[]
        {
            KeyDown(VK_SPACE),
            KeyUp(VK_SPACE)
        };

        // Clear previous error
        SetLastError(0);

        uint result = SendInput((uint)testInput.Length, testInput, Marshal.SizeOf(typeof(INPUT)));
        int error = Marshal.GetLastWin32Error();

        Console.WriteLine($"SendInput test: {result}/{testInput.Length}, error: {error}");

        if (result == 0 || result < testInput.Length)
        {
            // Check common error codes
            switch (error)
            {
                case 0:
                    Console.WriteLine("Error 0: Likely UIPI (User Interface Privilege Isolation) blocking");
                    break;
                case 5:
                    Console.WriteLine("Error 5: Access denied - run as Administrator");
                    break;
                case 87:
                    Console.WriteLine("Error 87: Invalid parameter - INPUT structure issue");
                    break;
                default:
                    Console.WriteLine($"Unknown error: {error}");
                    break;
            }
            return false; // Failed
        }

        // If test worked, send the full sequence
        Console.WriteLine("SendInput working, sending full sequence...");
        var inputs = new INPUT[]
        {
            KeyDown(VK_LWIN),
            KeyDown(VK_MENU),
            KeyDown(VK_SPACE),
            KeyUp(VK_SPACE),
            KeyUp(VK_MENU),
            KeyUp(VK_LWIN)
        };

        // Tag all inputs
        for (int i = 0; i < inputs.Length; i++)
            inputs[i].U.ki.dwExtraInfo = OUR_TAG;

        SetLastError(0);
        uint fullResult = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        int fullError = Marshal.GetLastWin32Error();

        Console.WriteLine($"Full SendInput result: {fullResult}/{inputs.Length}, error: {fullError}");

        return fullResult == inputs.Length && fullResult > 0;
    }

    static void TryKeybdEventMethod()
    {
        Console.WriteLine("=== FALLBACK: Using keybd_event for Win+Alt+Space ===");

        // Use legacy keybd_event API
        try
        {
            keybd_event((byte)VK_LWIN, 0, 0, OUR_TAG);           // Win down
            System.Threading.Thread.Sleep(5);
            keybd_event((byte)VK_MENU, 0, 0, OUR_TAG);           // Alt down  
            System.Threading.Thread.Sleep(5);
            keybd_event((byte)VK_SPACE, 0, 0, OUR_TAG);          // Space down
            System.Threading.Thread.Sleep(5);
            keybd_event((byte)VK_SPACE, 0, KEYEVENTF_KEYUP, OUR_TAG);  // Space up
            System.Threading.Thread.Sleep(5);
            keybd_event((byte)VK_MENU, 0, KEYEVENTF_KEYUP, OUR_TAG);   // Alt up
            System.Threading.Thread.Sleep(5);
            keybd_event((byte)VK_LWIN, 0, KEYEVENTF_KEYUP, OUR_TAG);   // Win up

            Console.WriteLine("keybd_event Win+Alt+Space sequence sent successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"keybd_event failed: {ex.Message}");
        }
    }

    static void SimulateWinKeySequence()
    {
        Console.WriteLine("Simulating Win+keys sequence...");

        // Try SendInput first, fall back to keybd_event
        if (!TrySendInputWinSequence())
        {
            Console.WriteLine("SendInput failed, trying keybd_event for Win sequence...");
            TryKeybdEventWinSequence();
        }
    }

    static bool TrySendInputWinSequence()
    {
        var inputs = new List<INPUT>();

        // Start with Win key down
        inputs.Add(KeyDown(_winKeyPressed));

        // Replay all recorded events in order
        foreach (var keyEvent in _keysWhileWinDown)
        {
            if (keyEvent.IsDown)
                inputs.Add(KeyDown(keyEvent.VirtualKey));
            else
                inputs.Add(KeyUp(keyEvent.VirtualKey));
        }

        // End with Win key up
        inputs.Add(KeyUp(_winKeyPressed));

        Console.WriteLine($"Attempting to send {inputs.Count} input events via SendInput");

        // Clear error and try
        SetLastError(0);
        var inputArray = inputs.ToArray();

        // Tag all inputs
        for (int i = 0; i < inputArray.Length; i++)
            inputArray[i].U.ki.dwExtraInfo = OUR_TAG;

        uint sent = SendInput((uint)inputArray.Length, inputArray, Marshal.SizeOf(typeof(INPUT)));
        int error = Marshal.GetLastWin32Error();

        Console.WriteLine($"SendInput Win sequence result: {sent}/{inputArray.Length}, error: {error}");

        // Return true only if we successfully sent all events
        bool success = sent == inputArray.Length && sent > 0;

        if (!success)
        {
            Console.WriteLine($"SendInput Win sequence failed - will try keybd_event fallback");
        }

        return success;
    }

    static void TryKeybdEventWinSequence()
    {
        Console.WriteLine("=== FALLBACK: Using keybd_event for Win+key sequence ===");

        try
        {
            // Win key down
            Console.WriteLine($"  keybd_event: Win({_winKeyPressed:X}) DOWN");
            keybd_event((byte)_winKeyPressed, 0, 0, OUR_TAG);
            System.Threading.Thread.Sleep(5);

            // All other keys in sequence
            foreach (var keyEvent in _keysWhileWinDown)
            {
                uint flags = keyEvent.IsDown ? 0u : KEYEVENTF_KEYUP;
                Console.WriteLine($"  keybd_event: {keyEvent.VirtualKey:X} {(keyEvent.IsDown ? "DOWN" : "UP")}");
                keybd_event((byte)keyEvent.VirtualKey, 0, flags, OUR_TAG);
                System.Threading.Thread.Sleep(5);
            }

            // Win key up
            Console.WriteLine($"  keybd_event: Win({_winKeyPressed:X}) UP");
            keybd_event((byte)_winKeyPressed, 0, KEYEVENTF_KEYUP, OUR_TAG);

            Console.WriteLine("keybd_event Win sequence completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"keybd_event Win sequence failed: {ex.Message}");
        }
    }

    static void SendInputSequence(INPUT[] inputs)
    {
        // Clear any previous error
        SetLastError(0);

        // Tag all inputs
        for (int i = 0; i < inputs.Length; i++)
            inputs[i].U.ki.dwExtraInfo = OUR_TAG;

        // Debug: show what we're about to send
        Console.WriteLine("Sending input sequence:");
        for (int i = 0; i < inputs.Length; i++)
        {
            var input = inputs[i];
            var ki = input.U.ki;
            string action = (ki.dwFlags & 2) != 0 ? "UP" : "DOWN";
            Console.WriteLine($"  {i}: Key {ki.wVk:X} {action}");
        }

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        Console.WriteLine($"SendInput result: {sent}/{inputs.Length}");

        if (sent == 0)
        {
            int error = Marshal.GetLastWin32Error();
            Console.WriteLine($"SendInput failed with error: {error}");

            // Check if we're running with proper privileges
            if (error == 5) // ERROR_ACCESS_DENIED
            {
                Console.WriteLine("ERROR: Access denied. Make sure you're running as Administrator!");
            }
        }
        else if (sent < inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            Console.WriteLine($"SendInput partially failed. Error: {error}");
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
