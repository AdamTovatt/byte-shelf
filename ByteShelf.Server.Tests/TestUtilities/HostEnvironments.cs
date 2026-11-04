using Microsoft.Extensions.Hosting;
using Moq;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Host environments for code that behaves by environment, such as what reaches the log.</summary>
    public static class HostEnvironments
    {
        public static IHostEnvironment Named(string name)
        {
            return Mock.Of<IHostEnvironment>(environment => environment.EnvironmentName == name);
        }
    }
}
