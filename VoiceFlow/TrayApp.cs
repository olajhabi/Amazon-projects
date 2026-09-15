using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VoiceFlow
{
    /// <summary>
    /// Orchestrates keyboard hook → audio → whisper → paste.
    /// Constructed on the UI thread from MessagePumpForm.OnLoad,
    /// so SetWindowsHookEx has a live message loop available.
    /// </summary>
    public sealed class TrayApp : IDisposable
    {
        private readonly Form _pump;           // MessagePumpForm — used for Invoke
        private readonly NotifyIcon _trayIcon;
        private readonly KeyboardHook _hook;
        private readonly AudioRecorder _recorder;
        private readonly WhisperTranscriber _transcriber;
        private readonly PillForm _pill;

        private AppState _state = AppState.Idle;
        private bool _busy = false;

        private readonly Icon _iconIdle;
        private readonly Icon _iconRecording;
        private readonly Icon _iconTranscribing;
        private readonly Icon _iconError;

        private static readonly string AppLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceFlow", "app.log");

        private static void Log(string msg)
        {
            try { File.AppendAllText(AppLog, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); }
            catch { }
        }

        public TrayApp(Form pump)
        {
            _pump = pump;

            _iconIdle         = MakeIcon(Color.Gray);
            _iconRecording    = MakeIcon(Color.FromArgb(220, 50, 50));
            _iconTranscribing = MakeIcon(Color.FromArgb(60, 180, 255));
            _iconError        = MakeIcon(Color.Orange);

            _pill = new PillForm();
            // Force handle creation while hidden
            _ = _pill.Handle;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Start at Login", null, (_, _) => StartAtLogin.Toggle());
            menu.Items.Add("-");
            menu.Items.Add("Quit VoiceFlow", null, (_, _) => ExitApp());

            _trayIcon = new NotifyIcon
            {
                Icon             = _iconIdle,
                Text             = "VoiceFlow — Idle\nHold Right Alt to record",
                Visible          = true,
                ContextMenuStrip = menu
            };

            _recorder = new AudioRecorder();
            _recorder.RecordingTimedOut += (_, _) => OnRecordingStopped();

            _transcriber = new WhisperTranscriber();

            _hook = new KeyboardHook();
            _hook.RecordingStarted   += (_, _) => OnRecordingStarted();
            _hook.RecordingStopped   += (_, _) => OnRecordingStopped();
            _hook.RecordingCancelled += (_, _) => OnRecordingCancelled();

            // We are already on the UI thread (called from OnLoad).
            // Install the hook now — message loop is live.
            _hook.Install();
            Log("TrayApp initialized. WhisperReady=" + WhisperTranscriber.IsReady());

            if (!WhisperTranscriber.IsReady())
            {
                SetState(AppState.Error);
                _trayIcon.ShowBalloonTip(5000, "VoiceFlow — Setup needed",
                    "whisper.cpp not found. Run setup.ps1 in the app folder.", ToolTipIcon.Warning);
            }
        }

        private void OnRecordingStarted()
        {
            Log($"OnRecordingStarted busy={_busy}");
            if (_busy) return;
            SetState(AppState.Recording);
            _recorder.StartRecording();
        }

        private void OnRecordingStopped()
        {
            Log($"OnRecordingStopped state={_state}");
            if (_state != AppState.Recording) return;
            _busy = true;
            SetState(AppState.Transcribing);
            var wavPath = _recorder.StopRecording();

            _ = Task.Run(async () =>
            {
                try
                {
                    var transcript = await _transcriber.TranscribeAsync(wavPath);
                    if (!string.IsNullOrWhiteSpace(transcript))
                    {
                        await (Task)_pump.Invoke(async () =>
                            await ClipboardPaster.PasteTextAsync(transcript))!;
                    }
                    SetState(AppState.Idle);
                }
                catch (Exception ex)
                {
                    Log($"Transcription error: {ex}");
                    SetState(AppState.Error);
                    _pump.Invoke(() =>
                        _trayIcon.ShowBalloonTip(4000, "VoiceFlow — Error",
                            ex.Message, ToolTipIcon.Error));

                    await Task.Delay(4000);
                    if (_state == AppState.Error) SetState(AppState.Idle);
                }
                finally
                {
                    _busy = false;
                }
            });
        }

        private void OnRecordingCancelled()
        {
            if (_state == AppState.Recording)
            {
                _recorder.StopRecording();
                SetState(AppState.Idle);
            }
        }

        private void SetState(AppState state)
        {
            Log($"SetState {_state} -> {state}");
            _state = state;

            var (icon, tooltip) = state switch
            {
                AppState.Idle         => (_iconIdle,         "VoiceFlow — Idle\nHold Right Alt to record"),
                AppState.Recording    => (_iconRecording,    "VoiceFlow — Recording…"),
                AppState.Transcribing => (_iconTranscribing, "VoiceFlow — Transcribing…"),
                AppState.Error        => (_iconError,        "VoiceFlow — Error (check tray)"),
                _                     => (_iconIdle,         "VoiceFlow")
            };

            if (_pump.IsHandleCreated)
            {
                try { _pump.Invoke(() => { _trayIcon.Icon = icon; _trayIcon.Text = tooltip; }); }
                catch { }
            }
            else
            {
                _trayIcon.Icon = icon;
                _trayIcon.Text = tooltip;
            }

            _pill.SetState(state);
        }

        public void ExitApp()
        {
            _hook.Dispose();
            _recorder.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _pill.Dispose();
            Application.Exit();
        }

        private static Icon MakeIcon(Color color)
        {
            var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 12, 12);
            return Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            _hook.Dispose();
            _recorder.Dispose();
            _trayIcon.Dispose();
            _pill.Dispose();
            _iconIdle.Dispose();
            _iconRecording.Dispose();
            _iconTranscribing.Dispose();
            _iconError.Dispose();
        }
    }
}
