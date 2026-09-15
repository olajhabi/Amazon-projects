using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace VoiceFlow
{
    /// <summary>
    /// Shells out to whisper.cpp CLI to transcribe a WAV file.
    /// Expects whisper-cli.exe and ggml-small.bin to be in the "whisper" subfolder
    /// next to the app executable.
    /// </summary>
    public sealed class WhisperTranscriber
    {
        private static readonly string AppDir =
            AppContext.BaseDirectory;

        public static readonly string WhisperDir =
            Path.Combine(AppDir, "whisper");

        public static readonly string WhisperExe =
            Path.Combine(WhisperDir, "whisper-cli.exe");

        public static readonly string ModelPath =
            Path.Combine(WhisperDir, "ggml-small.bin");

        /// <summary>
        /// Returns the transcribed text, or throws on failure.
        /// </summary>
        public async Task<string> TranscribeAsync(string wavPath)
        {
            if (!File.Exists(WhisperExe))
                throw new FileNotFoundException(
                    $"whisper-cli.exe not found at {WhisperExe}. Run setup.ps1 first.");

            if (!File.Exists(ModelPath))
                throw new FileNotFoundException(
                    $"Model file not found at {ModelPath}. Run setup.ps1 first.");

            // Write output to a temp file so we don't have to parse stdout mixing progress logs
            var outFile = Path.Combine(Path.GetTempPath(), $"vf_{Guid.NewGuid():N}.txt");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = WhisperExe,
                    // -m model -f input -otxt -of outfile -np (no progress) -nt (no timestamps)
                    Arguments = $"-m \"{ModelPath}\" -f \"{wavPath}\" -otxt -of \"{outFile}\" -np -nt",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = WhisperDir
                };

                using var process = new Process { StartInfo = psi };
                var stderr = new StringBuilder();
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                process.Start();
                process.BeginErrorReadLine();

                // Timeout after 60 seconds
                var completed = await Task.Run(() => process.WaitForExit(60_000));
                if (!completed)
                {
                    process.Kill();
                    throw new TimeoutException("whisper.cpp timed out after 60 seconds.");
                }

                if (process.ExitCode != 0)
                    throw new Exception($"whisper-cli exited with code {process.ExitCode}.\n{stderr}");

                // whisper -otxt writes to outfile.txt
                var txtFile = outFile + ".txt";
                if (!File.Exists(txtFile))
                    throw new FileNotFoundException($"whisper output not found at {txtFile}");

                var transcript = (await File.ReadAllTextAsync(txtFile)).Trim();
                File.Delete(txtFile);
                return transcript;
            }
            finally
            {
                if (File.Exists(outFile)) File.Delete(outFile);
            }
        }

        public static bool IsReady() =>
            File.Exists(WhisperExe) && File.Exists(ModelPath);
    }
}
