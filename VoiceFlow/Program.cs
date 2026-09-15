using System;
using System.Threading;
using System.Windows.Forms;

namespace VoiceFlow
{
    internal static class Program
    {
        private static Mutex? _mutex;

        [STAThread]
        static void Main()
        {
            _mutex = new Mutex(true, "VoiceFlow_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("VoiceFlow is already running.\nCheck the system tray.",
                    "VoiceFlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);

            Application.ThreadException += (_, e) =>
                MessageBox.Show(e.Exception.Message, "VoiceFlow — Unhandled Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    MessageBox.Show(ex.Message, "VoiceFlow — Fatal Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            // Use a hidden pump form as the main form so the WinForms message loop
            // stays alive and active — required for WH_KEYBOARD_LL to receive callbacks.
            var pump = new MessagePumpForm();
            Application.Run(pump);

            _mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Invisible form whose sole purpose is to keep the WinForms message loop
    /// running and to host the TrayApp lifecycle.
    /// </summary>
    internal sealed class MessagePumpForm : Form
    {
        private TrayApp? _trayApp;

        public MessagePumpForm()
        {
            // Make the form completely invisible
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            Opacity         = 0;
            Size            = new System.Drawing.Size(1, 1);
            WindowState     = FormWindowState.Minimized;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // Hide immediately
            Hide();

            // Instantiate TrayApp here — we're on the UI thread with a live message loop
            _trayApp = new TrayApp(this);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _trayApp?.Dispose();
            base.OnFormClosed(e);
        }

        // Keep form invisible even if something tries to show it
        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }
    }
}
