using System.Reflection;
using ByteShelf.Server.Access.Controllers;
using ByteShelf.Server.Access.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ByteShelf.Server.Tests.Access
{
    /// <summary>
    /// Every action on a shelf's routes asks for exactly the operation the table in Access.md gives it,
    /// so none is left reachable by anyone signed in, by nobody at all, or by too weak a caller.
    /// </summary>
    public class ShelfAccessCoverageTests
    {
        private static readonly IReadOnlyDictionary<string, ShelfOperation> Expected = new Dictionary<string, ShelfOperation>
        {
            ["EntriesController.List"] = ShelfOperation.Read,
            ["EntriesController.GetFile"] = ShelfOperation.Read,
            ["EntriesController.GetContent"] = ShelfOperation.Read,
            ["EntriesController.PutContent"] = ShelfOperation.Add,
            ["EntriesController.SwapContent"] = ShelfOperation.Write,
            ["EntriesController.CreateDownloadLink"] = ShelfOperation.Read,
            ["EntriesController.CreateUploadLink"] = ShelfOperation.Add,
            ["EntriesController.DeleteFile"] = ShelfOperation.Write,
            ["EntriesController.MoveFile"] = ShelfOperation.Write,
            ["EntriesController.SetDescription"] = ShelfOperation.Write,
            ["EntriesController.Search"] = ShelfOperation.Read,
            ["EntriesController.ListTree"] = ShelfOperation.Read,
            ["DirectoriesController.Create"] = ShelfOperation.Add,
            ["DirectoriesController.Move"] = ShelfOperation.Write,
            ["DirectoriesController.Delete"] = ShelfOperation.Write,
            ["DirectoriesController.GetEncryption"] = ShelfOperation.Read,
            ["DirectoriesController.Encrypt"] = ShelfOperation.Write,
            ["DirectoriesController.RemoveEncryption"] = ShelfOperation.Write,
            ["UploadsController.Start"] = ShelfOperation.Add,
            ["UploadsController.StartSwap"] = ShelfOperation.Write,
            ["UploadsController.Get"] = ShelfOperation.Add,
            ["UploadsController.Append"] = ShelfOperation.Add,
            ["UploadsController.Complete"] = ShelfOperation.Add,
            ["UploadsController.Abort"] = ShelfOperation.Add,
            ["TrashController.List"] = ShelfOperation.Read,
            ["TrashController.ListDeletion"] = ShelfOperation.Read,
            ["TrashController.GetDeletedContent"] = ShelfOperation.Read,
            ["TrashController.Restore"] = ShelfOperation.Write,
            ["TrashController.EmptyTrash"] = ShelfOperation.Write,
            ["ShelvesController.Get"] = ShelfOperation.Read,
            ["ShelvesController.Update"] = ShelfOperation.Administer,
            ["ShelvesController.Delete"] = ShelfOperation.Administer,
            ["ShelfGrantsController.List"] = ShelfOperation.Administer,
            ["ShelfGrantsController.Set"] = ShelfOperation.Administer,
            ["ShelfGrantsController.Remove"] = ShelfOperation.Administer,
            ["ShelfKeysController.List"] = ShelfOperation.Administer,
            ["ShelfKeysController.Create"] = ShelfOperation.Administer,
            ["ShelfKeysController.Delete"] = ShelfOperation.Administer,
        };

        [Fact]
        public void EveryActionOnAShelfRoute_AsksForTheOperationItIsListedWith()
        {
            List<string> found = new List<string>();

            foreach (Type controller in typeof(Program).Assembly.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract))
            {
                string controllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;

                foreach (MethodInfo action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    IEnumerable<string> templates = action.GetCustomAttributes<HttpMethodAttribute>().Select(attribute => controllerRoute + "/" + attribute.Template);
                    if (templates.Any(template => template.Contains("{shelf}", StringComparison.Ordinal)))
                    {
                        ShelfOperation? operation = action.GetCustomAttribute<ShelfAccessAttribute>()?.Operation;
                        found.Add($"{controller.Name}.{action.Name}: {operation?.ToString() ?? "unguarded"}");
                    }
                }
            }

            Assert.Equal(
                Expected.Select(pair => $"{pair.Key}: {pair.Value}").Order(StringComparer.Ordinal),
                found.Order(StringComparer.Ordinal));
        }
    }
}
