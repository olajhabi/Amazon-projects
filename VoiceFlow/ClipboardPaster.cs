using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VoiceFlow
{
    /// <summary>
    /// Saves the current clipboard, sets the transcript text, synthesizes Ctrl+V,
    /// then restores the original clipboard ~600ms later — same pattern as the macOS app.
    /// Must be called on the UI thread (or via STA thread) for clipboard access.
    /// </summary>
    public static class ClipboardPaster
    {
        public static async Task PasteTextAsync(string text)
        {
            // Capture existing clipboard content
            string? previousText = null;
            bool hadText = false;

            try
            {
                if (Clipboard.ContainsText())
                {
                    previousText = Clipboard.GetText();
                    hadText = true;
                }
            }
            catch { /* ignore — clipboard may be locked */ }

            // Set transcript to clipboard
            Clipboard.SetText(text);

            // Small delay so clipboard is ready before keypress
            await Task.Delay(50);

            // Synthesize Ctrl+V
            SendCtrlV();

            // Restore original clipboard after 600ms
            _ = Task.Run(async () =>
            {
                await Task.Delay(600);
                try
                {
                    // Must run on STA thread
                    var thread = new Thread(() =>
                    {
                        if (hadText && previousText != null)
                            Clipboard.SetText(previousText);
                        else
                            Clipboard.Clear();
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join();
                }
                catch { /* ignore restore failure */ }
            });
        }

        private static void SendCtrlV()
        {
            var inputs = new NativeMethods.INPUT[]
            {
                // Ctrl down
                new() {
                    type = NativeMethods.INPUT_KEYBOARD,
                    U = new NativeMethods.InputUnion {
                        ki = new NativeMethods.KEYBDINPUT {
                            wVk = NativeMethods.VK_CONTROL,
                            dwFlags = 0
                        }
                    }
                },
                // V down
                new() {
                    type = NativeMethods.INPUT_KEYBOARD,
                    U = new NativeMethods.InputUnion {
                        ki = new NativeMethods.KEYBDINPUT {
                            wVk = NativeMethods.VK_V,
                            dwFlags = 0
                        }
                    }
                },
                // V up
                new() {
                    type = NativeMethods.INPUT_KEYBOARD,
                    U = new NativeMethods.InputUnion {
                        ki = new NativeMethods.KEYBDINPUT {
                            wVk = NativeMethods.VK_V,
                            dwFlags = NativeMethods.KEYEVENTF_KEYUP
                        }
                    }
                },
                // Ctrl up
                new() {
                    type = NativeMethods.INPUT_KEYBOARD,
                    U = new NativeMethods.InputUnion {
                        ki = new NativeMethods.KEYBDINPUT {
                            wVk = NativeMethods.VK_CONTROL,
                            dwFlags = NativeMethods.KEYEVENTF_KEYUP
                        }
                    }
                }
            };

            NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        }
    }
}
