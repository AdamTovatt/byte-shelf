using System.Runtime.CompilerServices;

namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    internal static class PreparedCodecs
    {
        /// <summary>Before a test uses ImageMagick itself, as it takes its policy once: the tests know no more formats than the plugin does.</summary>
        [ModuleInitializer]
        internal static void Prepare()
        {
            Codecs.Prepare();
        }
    }
}
