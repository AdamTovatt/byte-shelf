using System.Text.Json;
using System.Text.Json.Serialization;

namespace ByteShelf.Server.Shared.Json
{
    /// <summary>
    /// The single home for the app's JSON conventions. MVC's serializer picks them up via
    /// <see cref="Configure"/> in Program.cs; hand-serialized payloads (the exception middleware,
    /// the SSE stream) use <see cref="CamelCase"/> — both paths share the same converters, so a
    /// payload serializes identically wherever it's produced.
    /// </summary>
    public static class JsonDefaults
    {
        public static readonly JsonSerializerOptions CamelCase = CreateCamelCase();

        /// <summary>
        /// Applies the shared converters to an existing options instance (MVC already camelCases
        /// property names by default; only the converters need adding there).
        /// </summary>
        public static void Configure(JsonSerializerOptions options)
        {
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        }

        private static JsonSerializerOptions CreateCamelCase()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            Configure(options);
            return options;
        }
    }
}
