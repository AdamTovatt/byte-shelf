using ByteShelf.Server.Plugins.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ByteShelf.Server.Plugins.Controllers
{
    /// <summary>
    /// Puts every route of a plugin's controllers under <c>/api/plugins/{id}/</c> whatever they declare, so
    /// no plugin's controller can take an address of the server's or of another plugin, and keeps them out
    /// of the OpenAPI document, which describes the server's own API. It changes the finished actions, as
    /// the last of their providers, rather than the controller models: a controller's own convention
    /// attributes run after every convention of the application.
    /// </summary>
    internal sealed class PluginRouteProvider : IActionDescriptorProvider
    {
        // The providers are run through a second time in descending order, which ends with this one.
        public int Order => int.MinValue;

        public void OnProvidersExecuting(ActionDescriptorProviderContext context)
        {
        }

        public void OnProvidersExecuted(ActionDescriptorProviderContext context)
        {
            foreach (ControllerActionDescriptor action in context.Results.OfType<ControllerActionDescriptor>())
            {
                if (PluginLoadContext.PluginIdOf(action.ControllerTypeInfo.Assembly) is { } pluginId)
                {
                    Confine(action, $"api/plugins/{pluginId}");
                }
            }
        }

        private static void Confine(ControllerActionDescriptor action, string prefix)
        {
            action.Properties.Remove(typeof(ApiDescriptionActionData));
            if (action.AttributeRouteInfo is { } route)
            {
                // Finished, a template is whole and starts without a slash, whichever way it was declared.
                route.Template = string.IsNullOrEmpty(route.Template) ? prefix : $"{prefix}/{route.Template}";
            }
        }
    }
}
