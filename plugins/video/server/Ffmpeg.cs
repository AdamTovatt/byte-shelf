using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ByteShelf.Plugins.Video
{
    /// <summary>
    /// The plugin's own ffmpeg and ffprobe (<c>ffmpeg/&lt;runtime&gt;/</c> beside its assembly, Ffmpeg.targets), which every
    /// run of either goes through, kept to reading the contents from <see cref="RangeServer"/> and from no other place.
    /// Of contents they cannot read they say nothing, by ending with an error.
    /// </summary>
    internal sealed class Ffmpeg
    {
        /// <summary>
        /// Containers a video comes in, by ffmpeg's demuxers. Others, such as a playlist (HLS, concat), would have ffmpeg
        /// open the addresses or files they name.
        /// </summary>
        private const string Containers = "mov,matroska,avi,mpegts,mpeg,flv,asf,ogg,mxf,dv";

        /// <summary>More than ffprobe says of any video; past it, what is read is not taken.</summary>
        private const int MaxAnswerChars = 4 * 1024 * 1024;

        private const int AnswerChunkChars = 16 * 1024;

        private Ffmpeg(string ffmpegPath, string ffprobePath)
        {
            FfmpegPath = ffmpegPath;
            FfprobePath = ffprobePath;
        }

        public string FfmpegPath { get; }

        public string FfprobePath { get; }

        /// <summary>The builds of this machine's platform.</summary>
        /// <exception cref="PlatformNotSupportedException">The plugin has none for it.</exception>
        /// <exception cref="FileNotFoundException">The plugin's folder lacks them.</exception>
        public static Ffmpeg Locate()
        {
            return Locate(RuntimeOfThisMachine(), Path.GetDirectoryName(typeof(Ffmpeg).Assembly.Location)!);
        }

        internal static Ffmpeg Locate(string? runtime, string pluginFolder)
        {
            if (runtime == null)
            {
                throw new PlatformNotSupportedException("The video plugin has ffmpeg for Linux x64 and arm64 alone.");
            }

            string folder = Path.Combine(pluginFolder, "ffmpeg", runtime);
            Ffmpeg found = new Ffmpeg(Path.Combine(folder, "ffmpeg"), Path.Combine(folder, "ffprobe"));
            foreach (string tool in new[] { found.FfmpegPath, found.FfprobePath })
            {
                if (!File.Exists(tool))
                {
                    throw new FileNotFoundException($"The video plugin's {Path.GetFileName(tool)} is not in its folder.", tool);
                }
            }

            return found;
        }

        internal static string? RuntimeOfThisMachine()
        {
            if (!OperatingSystem.IsLinux())
            {
                return null;
            }

            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => null,
            };
        }

        /// <summary>
        /// What ffmpeg or ffprobe takes as its input: <paramref name="url"/>, over HTTP alone and in a container of
        /// <see cref="Containers"/>, asked <paramref name="requestBytes"/> at a time: what it asked for open-ended would be
        /// read ahead of it into the loopback's buffers, megabytes of each seek.
        /// </summary>
        public static IReadOnlyList<string> Input(string url, int requestBytes)
        {
            return
            [
                "-protocol_whitelist", "http,tcp", "-format_whitelist", Containers,
                "-request_size", requestBytes.ToString(CultureInfo.InvariantCulture), "-i", url,
            ];
        }

        /// <summary>Runs ffmpeg, and answers nothing: what it prints is thrown away.</summary>
        /// <exception cref="FfmpegFailedException">It ended with an error.</exception>
        public async Task RunFfmpegAsync(IEnumerable<string> arguments)
        {
            await RunAsync(FfmpegPath, ["-nostdin", "-hide_banner", "-v", "error", .. arguments], maxAnswerChars: null);
        }

        /// <summary>What ffprobe prints.</summary>
        /// <exception cref="FfmpegFailedException">It ended with an error, or printed too much.</exception>
        public Task<string> RunFfprobeAsync(IEnumerable<string> arguments)
        {
            return RunFfprobeAsync(arguments, MaxAnswerChars);
        }

        /// <summary>What ffprobe prints, of at most <paramref name="maxAnswerChars"/>.</summary>
        internal Task<string> RunFfprobeAsync(IEnumerable<string> arguments, int maxAnswerChars)
        {
            return RunAsync(FfprobePath, ["-hide_banner", "-v", "error", .. arguments], maxAnswerChars);
        }

        /// <summary>
        /// Runs <paramref name="tool"/> to its end, and answers what it prints, of at most <paramref name="maxAnswerChars"/>;
        /// with no most, what it prints is thrown away and it answers nothing. What it writes to standard error is thrown
        /// away, as it may quote the contents. It inherits the process's CPU priority and memory limit (Processes.md).
        /// </summary>
        private static async Task<string> RunAsync(string tool, IReadOnlyList<string> arguments, int? maxAnswerChars)
        {
            ProcessStartInfo start = new ProcessStartInfo(tool)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(start) ?? throw new FfmpegFailedException(Path.GetFileName(tool), "could not start");
            try
            {
                process.StandardInput.Close();
                Task errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
                string? answer = maxAnswerChars is { } max
                    ? await ReadAnswerAsync(process.StandardOutput, max)
                    : await DiscardAsync(process.StandardOutput);
                await errors;
                await process.WaitForExitAsync();
                if (answer == null)
                {
                    throw new FfmpegFailedException(Path.GetFileName(tool), "said too much");
                }

                if (process.ExitCode != 0)
                {
                    throw new FfmpegFailedException(Path.GetFileName(tool), $"ended with {process.ExitCode}");
                }

                return answer;
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }

        /// <summary>All of <paramref name="output"/>; null when it is more than <paramref name="maxChars"/>.</summary>
        private static async Task<string?> ReadAnswerAsync(StreamReader output, int maxChars)
        {
            StringBuilder answer = new StringBuilder();
            char[] buffer = new char[AnswerChunkChars];
            int read;
            while (answer.Length <= maxChars && (read = await output.ReadAsync(buffer)) > 0)
            {
                answer.Append(buffer, 0, read);
            }

            if (answer.Length > maxChars)
            {
                await DiscardAsync(output);
                return null;
            }

            return answer.ToString();
        }

        private static async Task<string> DiscardAsync(StreamReader output)
        {
            await output.BaseStream.CopyToAsync(Stream.Null);
            return string.Empty;
        }
    }
}
