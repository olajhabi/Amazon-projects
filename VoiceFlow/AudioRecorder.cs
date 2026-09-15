using System;
using System.IO;
using NAudio.Wave;

namespace VoiceFlow
{
    /// <summary>
    /// Records microphone audio at 16kHz mono and writes a WAV file.
    /// Automatically stops after MaxDurationSeconds (default 60).
    /// </summary>
    public sealed class AudioRecorder : IDisposable
    {
        public const int MaxDurationSeconds = 60;
        public static readonly string CachePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "VoiceFlow", "last.wav");

        public event EventHandler? RecordingTimedOut;

        private WaveInEvent? _waveIn;
        private WaveFileWriter? _writer;
        private DateTime _startedAt;
        private bool _recording = false;

        public void StartRecording()
        {
            if (_recording) return;

            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);

            // Delete previous recording
            if (File.Exists(CachePath))
                File.Delete(CachePath);

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1), // 16kHz, 16-bit, mono
                BufferMilliseconds = 50
            };

            _writer = new WaveFileWriter(CachePath, _waveIn.WaveFormat);
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            _startedAt = DateTime.UtcNow;
            _recording = true;
            _waveIn.StartRecording();
        }

        public string StopRecording()
        {
            if (!_recording) return CachePath;
            _recording = false;
            _waveIn?.StopRecording();
            return CachePath;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);

            // Enforce 60-second cap
            if ((DateTime.UtcNow - _startedAt).TotalSeconds >= MaxDurationSeconds)
            {
                RecordingTimedOut?.Invoke(this, EventArgs.Empty);
                StopRecording();
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }

        public void Dispose()
        {
            if (_recording) StopRecording();
            _waveIn?.Dispose();
            _writer?.Dispose();
        }
    }
}
