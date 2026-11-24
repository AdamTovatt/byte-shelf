namespace ByteShelf.Server.Tests.Plugin
{
    /// <summary>Which copies of two assemblies the plugin runs with.</summary>
    /// <param name="OwnPackageVersion">The version of the package it brings itself.</param>
    /// <param name="CommonLocation">Where <c>ByteShelfCommon</c> was loaded from.</param>
    public sealed record PluginGreeting(string OwnPackageVersion, string CommonLocation);
}
