using Microsoft.Extensions.FileProviders;
using System.Reflection;
using System.Text;

namespace ByteShelf.Resources
{
    /// <summary>
    /// Helper class for accessing embedded resources within the ByteShelf application.
    /// </summary>
    /// <remarks>
    /// This class provides functionality to read embedded resources, determine their content types,
    /// and verify that all mapped resources exist as embedded files. It uses a singleton pattern
    /// to ensure consistent access to embedded resources throughout the application.
    /// </remarks>
    public class ResourceHelper
    {
        private static readonly Lazy<ResourceHelper> instance = new Lazy<ResourceHelper>(() => new ResourceHelper());
        
        /// <summary>
        /// Gets the singleton instance of the ResourceHelper.
        /// </summary>
        /// <remarks>
        /// This property provides access to the single instance of ResourceHelper,
        /// ensuring consistent resource access throughout the application.
        /// </remarks>
        public static ResourceHelper Instance => instance.Value;

        private readonly EmbeddedFileProvider fileProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceHelper"/> class.
        /// </summary>
        /// <remarks>
        /// This constructor creates an EmbeddedFileProvider for the current assembly
        /// to enable access to embedded resources.
        /// </remarks>
        private ResourceHelper()
        {
            fileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly());
        }

        /// <summary>
        /// Determines the MIME content type for a given resource based on its file extension.
        /// </summary>
        /// <param name="resource">The resource to determine the content type for.</param>
        /// <returns>The MIME content type string for the resource.</returns>
        /// <remarks>
        /// This method examines the file extension of the resource path and returns
        /// the appropriate MIME type. For unknown extensions, it defaults to
        /// "application/octet-stream".
        /// </remarks>
        public string GetContentType(Resource resource)
        {
            string extension = Path.GetExtension(resource.Path).ToLowerInvariant();

            return extension switch
            {
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".otf" => "font/otf",
                ".ttf" => "font/ttf",
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".pdf" => "application/pdf",
                ".cer" => "application/x-x509-ca-cert",
                ".onnx" => "application/octet-stream",
                _ => "application/octet-stream"
            };
        }

        /// <summary>
        /// Reads the content of an embedded resource as a string asynchronously.
        /// </summary>
        /// <param name="resource">The resource to read.</param>
        /// <returns>A task that represents the asynchronous read operation. The task result contains the resource content as a string.</returns>
        /// <remarks>
        /// This method opens the embedded resource as a stream, reads it to completion,
        /// and returns the content as a string. The stream is properly disposed of after use.
        /// </remarks>
        public async Task<string> ReadAsStringAsync(Resource resource)
        {
            using (Stream resourceStream = GetFileStream(resource))
            {
                using (StreamReader reader = new StreamReader(resourceStream))
                {
                    return await reader.ReadToEndAsync();
                }
            }
        }

        /// <summary>
        /// Gets a stream for reading an embedded resource.
        /// </summary>
        /// <param name="resource">The resource to get a stream for.</param>
        /// <returns>A stream that can be used to read the resource content.</returns>
        /// <remarks>
        /// This method returns a stream that can be used to read the embedded resource.
        /// The caller is responsible for disposing of the stream when finished.
        /// </remarks>
        public Stream GetFileStream(Resource resource)
        {
            return GetFileInfo(resource).CreateReadStream();
        }

        /// <summary>
        /// Gets file information for an embedded resource.
        /// </summary>
        /// <param name="resource">The resource to get information for.</param>
        /// <returns>An IFileInfo object containing information about the embedded resource.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified resource does not exist.</exception>
        /// <remarks>
        /// This method retrieves file information for an embedded resource, including
        /// whether it exists, its length, and other metadata. If the resource does not
        /// exist, a FileNotFoundException is thrown.
        /// </remarks>
        public IFileInfo GetFileInfo(Resource resource)
        {
            string fullPath = $"Resources/{resource.Path}";
            IFileInfo fileInfo = fileProvider.GetFileInfo(fullPath);

            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException($"Resource '{fullPath}' not found.");
            }

            return fileInfo;
        }

        /// <summary>
        /// Verifies that all mapped resources exist as embedded files and that all embedded files have corresponding mappings.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when there are mismatches between mapped resources and embedded files.</exception>
        /// <remarks>
        /// This method performs an integrity check to ensure:
        /// 1. All resources mapped in the Resource class exist as embedded files
        /// 2. All embedded files have corresponding mappings in the Resource class
        /// 
        /// This verification is performed on application startup to prevent runtime errors
        /// when trying to access embedded resources that don't exist.
        /// </remarks>
        public void VerifyResourceMappings()
        {
            StringBuilder errors = new StringBuilder();

            // 1. Ensure all mapped resources exist as embedded files
            HashSet<string> mappedResources = GetAllResourcePaths();
            foreach (string resourcePath in mappedResources)
            {
                string fullPath = $"Resources/{resourcePath}";
                IFileInfo fileInfo = fileProvider.GetFileInfo(fullPath);
                if (!fileInfo.Exists)
                {
                    errors.AppendLine($"❌ Missing Embedded Resource: {fullPath}");
                }
            }

            // 2. Ensure all embedded resources have a corresponding mapping
            HashSet<string> embeddedResources = GetEmbeddedResourcePaths();
            foreach (string embeddedFile in embeddedResources)
            {
                if (!mappedResources.Contains(embeddedFile))
                {
                    errors.AppendLine($"❌ Unmapped Embedded File: {embeddedFile}");
                }
            }

            // Throw exception if there are any mismatches
            if (errors.Length > 0)
            {
                StringBuilder errorMessageBuilder = new StringBuilder($"Resource mapping integrity check failed:\n{errors}");
                errorMessageBuilder.AppendLine("If an embedded resource is missing it means that an embedded resources from the Resources folder has been removed while the mapping for it in the Resource class remains.");
                errorMessageBuilder.AppendLine("If there is an unmapped embedded file it means that there is an embedded resource in the Resources folder that has not been mapped correctly in the Resource class.");
                errorMessageBuilder.AppendLine("This error message and check is to make sure that all expected embedded resources in the Resources folder exist.");
                errorMessageBuilder.AppendLine("It is performed when the tests run and on api startup to ensure no unexpected error can occur in the middle of resonding to a request or similar.");

                throw new InvalidOperationException(errorMessageBuilder.ToString());
            }
        }

        private static HashSet<string> GetAllResourcePaths()
        {
            HashSet<string> paths = new HashSet<string>();

            Type[] categories = typeof(Resource).GetNestedTypes();
            foreach (Type category in categories)
            {
                FieldInfo[] fields = category.GetFields(BindingFlags.Public | BindingFlags.Static);
                foreach (FieldInfo field in fields)
                {
                    object? fieldValue = field.GetValue(null);
                    if (fieldValue is Resource resource)
                    {
                        paths.Add(resource.Path);
                    }
                }
            }

            return paths;
        }

        private HashSet<string> GetAllResourceFolders()
        {
            HashSet<string> folders = new HashSet<string>();

            foreach (Type category in typeof(Resource).GetNestedTypes())
            {
                if (category.GetFields(BindingFlags.Public | BindingFlags.Static).Any(x => x.GetValue(null) is Resource))
                    folders.Add(category.Name);
            }

            return folders;
        }

        private HashSet<string> GetEmbeddedResourcePaths()
        {
            HashSet<string> embeddedResources = new HashSet<string>();

            Assembly assembly = Assembly.GetExecutingAssembly();
            string assemblyName = assembly.GetName().Name!; // Get namespace prefix

            // Get all embedded resources in the assembly
            string[] resourceNames = assembly.GetManifestResourceNames();

            // Collect known folder names from ResourcePath mappings
            HashSet<string> knownFolders = GetAllResourceFolders();

            foreach (string resource in resourceNames)
            {
                if (resource.StartsWith($"{assemblyName}.Resources"))
                {
                    // Remove "ByteShelf.Resources." prefix
                    string relativePath = resource.Substring($"{assemblyName}.Resources.".Length);

                    // Split on dots and try to reconstruct folder/file structure
                    string[] parts = relativePath.Split('.');
                    List<string> processedParts = new List<string>();

                    for (int i = 0; i < parts.Length; i++)
                    {
                        string currentPart = parts[i];
                        string constructedCheckPath = string.Join("/", processedParts) + "/" + currentPart;
                        if (processedParts.Count == 0 && constructedCheckPath.Length > 1) constructedCheckPath = constructedCheckPath.Substring(1);

                        // If this is a known folder, we treat it as a directory and use a "/"
                        if (knownFolders.Contains(constructedCheckPath))
                        {
                            processedParts.Add(currentPart);
                        }
                        else
                        {
                            // Assume remaining parts belong to the filename
                            string filename = string.Join(".", parts.Skip(i));
                            processedParts.Add(filename);
                            break;
                        }
                    }

                    string normalizedPath = string.Join("/", processedParts);
                    embeddedResources.Add(normalizedPath);
                }
            }

            return embeddedResources;
        }
    }
} 