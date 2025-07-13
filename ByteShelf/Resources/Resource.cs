namespace ByteShelf.Resources
{
    /// <summary>
    /// Represents an embedded resource path within the ByteShelf application.
    /// </summary>
    /// <remarks>
    /// This struct provides a type-safe way to reference embedded resources
    /// and ensures that resource paths are properly validated at compile time.
    /// </remarks>
    public readonly struct Resource
    {
        /// <summary>
        /// Gets the path to the embedded resource.
        /// </summary>
        /// <remarks>
        /// This path is relative to the Resources folder and is used to locate
        /// the embedded resource within the assembly.
        /// </remarks>
        public string Path { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Resource"/> struct.
        /// </summary>
        /// <param name="path">The path to the embedded resource.</param>
        private Resource(string path)
        {
            Path = path;
        }

        /// <summary>
        /// Creates a Resource instance from a string path.
        /// </summary>
        /// <remarks>
        /// Warning: This method is dangerous and should not be used unless the string parameter
        /// is from a serialized resource instance so that it's certain the resource exists.
        /// Should not be used unless it can be certain that the resource path is valid and exists
        /// since this can't be checked beforehand!
        /// </remarks>
        /// <param name="resourcePath">The verified resource path that is known to be of an existing resource that has then been serialized.</param>
        /// <returns>A resource instance.</returns>
        public static Resource Create(string resourcePath)
        {
            return new Resource(resourcePath);
        }

        /// <summary>
        /// Gets the filename component of the resource path.
        /// </summary>
        /// <returns>The filename extracted from the resource path.</returns>
        public string GetFileName()
        {
            return System.IO.Path.GetFileName(Path);
        }

        /// <summary>
        /// Returns the string representation of the resource path.
        /// </summary>
        /// <returns>The resource path as a string.</returns>
        public override string ToString() => Path;

        /// <summary>
        /// Implicit conversion operator from Resource to string.
        /// </summary>
        /// <param name="resourcePath">The resource to convert.</param>
        /// <returns>The resource path as a string.</returns>
        public static implicit operator string(Resource resourcePath) => resourcePath.Path;

        /// <summary>
        /// Contains frontend-related embedded resources.
        /// </summary>
        public static class Frontend
        {
            /// <summary>
            /// The main HTML frontend page for ByteShelf.
            /// </summary>
            public static readonly Resource ByteShelfFrontend = new Resource("Frontend/ByteShelfFrontend.html");

            /// <summary>
            /// The CSS styles for the ByteShelf frontend.
            /// </summary>
            public static readonly Resource ByteShelfStyles = new Resource("Frontend/ByteShelfStyles.css");

            /// <summary>
            /// The JavaScript code for the ByteShelf frontend.
            /// </summary>
            public static readonly Resource ByteShelfScript = new Resource("Frontend/ByteShelfScript.js");

            /// <summary>
            /// A large icon for ByteShelf.
            /// </summary>
            public static readonly Resource ByteShelfIcon256 = new Resource("Frontend/ByteShelfIcon256.png");
        }
    }
}