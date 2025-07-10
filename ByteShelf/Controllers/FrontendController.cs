using ByteShelf.Resources;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Controllers
{
    /// <summary>
    /// Controller for serving the embedded ByteShelf frontend.
    /// </summary>
    /// <remarks>
    /// This controller provides endpoints for serving the embedded frontend HTML, CSS, and JavaScript files.
    /// The frontend allows users to authenticate with API keys and manage their files.
    /// </remarks>
    [ApiController]
    [Route("")]
    public class FrontendController : ControllerBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FrontendController"/> class.
        /// </summary>
        public FrontendController()
        {
        }

        /// <summary>
        /// Serves the main ByteShelf frontend HTML page.
        /// </summary>
        /// <returns>The HTML content for the ByteShelf frontend.</returns>
        /// <response code="200">Returns the HTML content.</response>
        /// <response code="404">If the frontend resource is not found.</response>
        /// <remarks>
        /// This endpoint serves the main frontend HTML page that allows users to:
        /// - Authenticate with API keys
        /// - View tenant information and storage usage
        /// - Upload and manage files
        /// - Download and delete files
        /// </remarks>
        [HttpGet]
        public IActionResult GetFrontend()
        {
            try
            {
                string htmlContent = ResourceHelper.Instance.ReadAsStringAsync(Resource.Frontend.ByteShelfFrontend).Result;
                
                // Get the external URL from forwarded headers, but hardcode https
                string scheme = "https"; // Hardcoded to fix mixed content
                string host = Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.Value;
                string prefix = Request.Headers["X-Forwarded-Prefix"].FirstOrDefault() ?? "";
                
                string requestUrl = $"{scheme}://{host}{prefix}";
                htmlContent = htmlContent.Replace("http://localhost:5001", requestUrl);
                
                return Content(htmlContent, "text/html");
            }
            catch (Exception ex)
            {
                return NotFound($"Frontend not found: {ex.Message}");
            }
        }

        /// <summary>
        /// Serves the CSS styles for the ByteShelf frontend.
        /// </summary>
        /// <returns>The CSS content for styling the frontend.</returns>
        /// <response code="200">Returns the CSS content.</response>
        /// <response code="404">If the CSS resource is not found.</response>
        [HttpGet("styles.css")]
        public IActionResult GetStyles()
        {
            try
            {
                string cssContent = ResourceHelper.Instance.ReadAsStringAsync(Resource.Frontend.ByteShelfStyles).Result;
                return Content(cssContent, "text/css");
            }
            catch (Exception ex)
            {
                return NotFound($"Styles not found: {ex.Message}");
            }
        }

        /// <summary>
        /// Serves the JavaScript code for the ByteShelf frontend.
        /// </summary>
        /// <returns>The JavaScript content for frontend functionality.</returns>
        /// <response code="200">Returns the JavaScript content.</response>
        /// <response code="404">If the JavaScript resource is not found.</response>
        [HttpGet("script.js")]
        public IActionResult GetScript()
        {
            try
            {
                string jsContent = ResourceHelper.Instance.ReadAsStringAsync(Resource.Frontend.ByteShelfScript).Result;
                return Content(jsContent, "application/javascript");
            }
            catch (Exception ex)
            {
                return NotFound($"Script not found: {ex.Message}");
            }
        }

        /// <summary>
        /// Health check endpoint for the frontend.
        /// </summary>
        /// <returns>A simple health check response.</returns>
        /// <response code="200">Returns a health check message.</response>
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok(new { message = "ByteShelf Frontend is running", timestamp = DateTime.UtcNow });
        }
    }
} 