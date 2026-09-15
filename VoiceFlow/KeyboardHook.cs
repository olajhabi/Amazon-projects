using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace VoiceFlow
{
    /// <summary>
    /// Low-level keyboard hook on a dedicated STA thread running a raw
    /// Win32 GetMessage/DispatchMessage loop. This is the most reliable
    /// way to keep WH_KEYBOARD_LL callbacks alive independent of WinForms.
    /// </summary>
    public sealed class KeyboardHook : IDisposable
    {
        public event EventHandler? RecordingStarted;
        public event EventHandler? RecordingStopped;
        public event EventHandler? RecordingCancelled;

        public const int TapThresholdMs = 300;

        // VK_RMENU = 0xA5 (Right Alt)
        private const uint VK_TRIGGER = 0xA5;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN     = 0x0100;
        private const int WM_KEYUP       = 0x0101;
        private const int WM_SYSKEYDOWN  = 0x0104;
        private const int WM_SYSKEYUP    = 0x0105;
        private const int WM_QUIT        = 0x0012;

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceFlow", "keylog.txt");

        [DllImport("user32.dll")] private static extern bool GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint tid, uint msg, IntPtr wp, IntPtr lp);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd, wParam, lParam; public uint message, time; public int x, y; }

        private Thread? _pumpThread;
        private uint _pumpThreadId;
        private IntPtr _hookHandle = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc? _proc; // keep alive — GC guard
        private bool _keyDown;
        private DateTime _pressedAt;
        private readonly ManualResetEventSlim _ready = new(false);

        public void Install()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath, $"VoiceFlow key log started {DateTime.Now}\n");

            _pumpThread = new Thread(PumpLoop) { IsBackground = true, Name = "VoiceFlow.HookPump" };
            _pumpThread.SetApartmentState(ApartmentState.STA);
            _pumpThread.Start();

            _ready.Wait(5000);
            Log("Hook install complete from main thread side");
        }

        private void PumpLoop()
        {
            // Install hook on THIS thread — GetMessage loop below will service it
            _proc = HookCallback;
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            using var mod  = proc.MainModule!;
            _hookHandle = NativeMethods.SetWindowsHookEx(
                WH_KEYBOARD_LL, _proc,
                NativeMethods.GetModuleHandle(mod.ModuleName!), 0);

            if (_hookHandle == IntPtr.Zero)
                Log($"SetWindowsHookEx FAILED: {Marshal.GetLastWin32Error()}");
            else
                Log($"Hook installed on pump thread tid={Environment.CurrentManagedThreadId}. Handle={_hookHandle}");

            _pumpThreadId = GetCurrentThreadId();
            _ready.Set();

            // Raw Win32 message loop — keeps the thread alive and pumps hook callbacks
            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            // Cleanup
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
            Log("Pump thread exiting");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var kb  = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();

                Log($"vk=0x{kb.vkCode:X2}({kb.vkCode}) msg=0x{msg:X4} flags={kb.flags}");

                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isUp   = msg == WM_KEYUP   || msg == WM_SYSKEYUP;

                if (kb.vkCode == VK_TRIGGER)
                {
                    if (isDown && !_keyDown)
                    {
                        _keyDown   = true;
                        _pressedAt = DateTime.UtcNow;
                        Log(">>> RecordingStarted");
                        RecordingStarted?.Invoke(this, EventArgs.Empty);
                    }
                    else if (isUp && _keyDown)
                    {
                        _keyDown = false;
                        var ms = (DateTime.UtcNow - _pressedAt).TotalMilliseconds;
                        Log($">>> Released {ms:F0}ms");
                        if (ms >= TapThresholdMs)
                            RecordingStopped?.Invoke(this, EventArgs.Empty);
                        else
                            RecordingCancelled?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Uninstall()
        {
            if (_pumpThreadId != 0)
                PostThreadMessage(_pumpThreadId, (uint)WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        public void Dispose() => Uninstall();

        private static void Log(string msg)
        {
            try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); }
            catch { }
        }

        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    }
}
