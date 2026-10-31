using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Storage.Models;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Access.Controllers
{
    /// <summary>A controller under <c>api/shelves/{shelf}</c>, whose actions carry <see cref="ShelfAccessAttribute"/>.</summary>
    public abstract class ShelfControllerBase : ControllerBase
    {
        /// <summary>The caller's access to the shelf in the route.</summary>
        protected ShelfAccess Access => HttpContext.GetShelfAccess();

        /// <summary>The shelf in the route.</summary>
        protected Shelf Shelf => Access.Shelf;
    }
}
