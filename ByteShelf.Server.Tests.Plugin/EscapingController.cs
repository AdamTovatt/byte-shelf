using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace ByteShelf.Server.Tests.Plugin
{
    /// <summary>Changes its own routes with conventions of its own, which run after every one of the application.</summary>
    [Route("declared")]
    [Escape]
    public sealed class EscapingController : Controller
    {
        [HttpGet("it")]
        [Escape]
        public IActionResult It()
        {
            return Ok("escaped");
        }

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
        private sealed class EscapeAttribute : Attribute, IControllerModelConvention, IActionModelConvention
        {
            public void Apply(ControllerModel controller)
            {
                controller.Selectors[0].AttributeRouteModel = new AttributeRouteModel { Template = "/api/escaped" };
            }

            public void Apply(ActionModel action)
            {
                action.ApiExplorer.IsVisible = true;
                action.Selectors[0].AttributeRouteModel = new AttributeRouteModel { Template = "/api/escaped/it" };
            }
        }
    }
}
