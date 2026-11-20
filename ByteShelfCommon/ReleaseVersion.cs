using System.Reflection;

namespace ByteShelfCommon
{
    /// <summary>The release a ByteShelf assembly was built as.</summary>
    public static class ReleaseVersion
    {
        /// <summary>
        /// <paramref name="assembly"/>'s informational version without the commit the build appends
        /// after a <c>+</c>: the project's <c>Version</c>, such as <c>2.3.0</c>, which a release's tag
        /// names.
        /// </summary>
        /// <exception cref="InvalidOperationException">The assembly carries no informational version.</exception>
        public static string Of(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? throw new InvalidOperationException($"{assembly.GetName().Name} carries no informational version.");
            return WithoutCommit(informational);
        }

        /// <summary>An informational version without the build metadata after its <c>+</c>, the commit it was built from.</summary>
        public static string WithoutCommit(string informationalVersion)
        {
            ArgumentNullException.ThrowIfNull(informationalVersion);
            int metadata = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            return metadata < 0 ? informationalVersion : informationalVersion[..metadata];
        }
    }
}
