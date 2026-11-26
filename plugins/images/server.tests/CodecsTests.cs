using System.Diagnostics;
using System.Runtime.InteropServices;
using ByteShelf.Plugins.Images.Tests.TestUtilities;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests
{
    /// <summary>What ImageMagick may do where the plugin runs, whatever it were asked for.</summary>
    public sealed class CodecsTests
    {
        private static readonly MagickFormat[] Allowed =
            [MagickFormat.Jpeg, MagickFormat.Png, MagickFormat.Gif, MagickFormat.WebP, MagickFormat.Avif, MagickFormat.Heic, MagickFormat.Tiff, MagickFormat.Bmp, MagickFormat.Ico, MagickFormat.Dng];

        public static TheoryData<MagickFormat> Others { get; } = new TheoryData<MagickFormat>(Enum.GetValues<MagickFormat>().Except([.. Allowed, MagickFormat.Unknown]));

        [Theory]
        [MemberData(nameof(Others))]
        public void ReadsNoOtherFormat(MagickFormat format)
        {
            using MagickImage image = TestImages.Plain(16, 16, 255, 0, 0);
            byte[] contents = image.ToByteArray(MagickFormat.Png);

            MagickException refusal = Assert.ThrowsAny<MagickException>(() => new MagickImage(contents, new MagickReadSettings { Format = format, Width = 16, Height = 16 }));

            Assert.True(refusal is MagickPolicyErrorException or MagickMissingDelegateErrorException, $"{format}: {refusal.Message}");
        }

        /// <summary>One of ImageMagick's own pictures, as are its many more: it would be drawn with no file.</summary>
        [Fact]
        public void DrawsNothingOfItsOwn()
        {
            Assert.Throws<MagickMissingDelegateErrorException>(() => new MagickImage("xc:red"));
        }

        [Fact]
        public void LimitsWhatItTakes()
        {
            Assert.Equal((120_000_001ul, 1ul << 30, 0ul), (ResourceLimits.Area, ResourceLimits.Memory, ResourceLimits.Disk));
        }

        [LinuxFact]
        public async Task Prepare_TakesThePluginsPolicy()
        {
            (int exitCode, _) = await PrepareBesideAsync(null);

            Assert.Equal(0, exitCode);
        }

        /// <summary>Another policy ImageMagick finds is added to the plugin's: from /tmp/ImageMagick, or, as here, from the user's own configuration.</summary>
        [LinuxTheory]
        [InlineData("<policy domain='coder' rights='read | write' pattern='TXT'/><policy domain='module' rights='read | write' pattern='TXT'/>", "it reads Txt")]
        [InlineData("<policy domain='coder' rights='none' pattern='AVIF'/>", "it does not read Avif")]
        [InlineData("<policy domain='resource' name='memory' value='1MiB'/>", "limited otherwise")]
        [InlineData("<policy domain='resource' name='width' value='100'/>", "limited otherwise")]
        public async Task Prepare_RefusesAnotherPolicyBesideThePlugins(string policies, string expected)
        {
            (int exitCode, string error) = await PrepareBesideAsync($"<policymap>{policies}</policymap>");

            Assert.NotEqual(0, exitCode);
            Assert.Contains(expected, error, StringComparison.Ordinal);
        }

        private static async Task<(int ExitCode, string Error)> PrepareBesideAsync(string? policy)
        {
            string home = Directory.CreateTempSubdirectory("byteshelf-images-").FullName;
            try
            {
                if (policy != null)
                {
                    Directory.CreateDirectory(Path.Combine(home, "ImageMagick"));
                    await File.WriteAllTextAsync(Path.Combine(home, "ImageMagick", "policy.xml"), policy);
                }

                // The dotnet the tests run on, whatever is on PATH.
                string dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet"));
                ProcessStartInfo start = new ProcessStartInfo(dotnet, [typeof(CodecsTests).Assembly.Location]) { RedirectStandardError = true };
                start.Environment["XDG_CONFIG_HOME"] = home;
                using Process process = Process.Start(start)!;
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                return (process.ExitCode, error);
            }
            finally
            {
                Directory.Delete(home, recursive: true);
            }
        }
    }
}
