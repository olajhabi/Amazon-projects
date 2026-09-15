using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VoiceFlow
{
    public enum AppState { Idle, Recording, Transcribing, Error }

    /// <summary>
    /// Borderless topmost pill window near the bottom of the screen.
    /// Shows recording/transcribing state visually — same as the macOS floating panel.
    /// </summary>
    public sealed class PillForm : Form
    {
        private Label _label = null!;
        private Panel _dot = null!;
        private System.Windows.Forms.Timer _pulseTimer = null!;
        private bool _pulseUp = true;
        private int _pulseAlpha = 255;

        public PillForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(30, 30, 30);
            TransparencyKey = Color.Empty;
            Opacity = 0.92;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(220, 44);
            Region = RoundedRegion(Width, Height, 22);

            // Dot indicator
            _dot = new Panel
            {
                Size = new Size(10, 10),
                Location = new Point(16, 17),
                BackColor = Color.Gray
            };
            _dot.Region = new Region(RoundedRectangle(0, 0, 10, 10, 5));

            // Status label
            _label = new Label
            {
                AutoSize = false,
                Size = new Size(170, 30),
                Location = new Point(32, 7),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = ""
            };

            Controls.Add(_dot);
            Controls.Add(_label);

            // Pulse timer for recording state
            _pulseTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _pulseTimer.Tick += PulseTick;

            // Position near bottom-center of primary screen
            RepositionOnScreen();
        }

        private void RepositionOnScreen()
        {
            var screen = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(
                screen.Left + (screen.Width - Width) / 2,
                screen.Bottom - Height - 40);
        }

        public void SetState(AppState state)
        {
            if (InvokeRequired) { Invoke(() => SetState(state)); return; }

            switch (state)
            {
                case AppState.Idle:
                    Hide();
                    _pulseTimer.Stop();
                    return;

                case AppState.Recording:
                    _label.Text = "Recording…";
                    _dot.BackColor = Color.FromArgb(220, 50, 50);
                    _pulseTimer.Start();
                    break;

                case AppState.Transcribing:
                    _label.Text = "Transcribing…";
                    _dot.BackColor = Color.FromArgb(60, 180, 255);
                    _pulseTimer.Stop();
                    _dot.BackColor = Color.FromArgb(60, 180, 255);
                    break;

                case AppState.Error:
                    _label.Text = "Error — check tray";
                    _dot.BackColor = Color.Orange;
                    _pulseTimer.Stop();
                    break;
            }

            RepositionOnScreen();
            Show();
            BringToFront();
        }

        private void PulseTick(object? sender, EventArgs e)
        {
            _pulseAlpha += _pulseUp ? 8 : -8;
            if (_pulseAlpha >= 255) { _pulseAlpha = 255; _pulseUp = false; }
            if (_pulseAlpha <= 80) { _pulseAlpha = 80; _pulseUp = true; }
            _dot.BackColor = Color.FromArgb(_pulseAlpha, 220, 50, 50);
        }

        private static Region RoundedRegion(int w, int h, int r)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(w - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(w - r * 2, h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }

        private static GraphicsPath RoundedRectangle(int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath();
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Region = RoundedRegion(Width, Height, 22);
        }

        // Make the window click-through so it doesn't steal focus
        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TRANSPARENT = 0x00000020;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
                return cp;
            }
        }
    }
}
