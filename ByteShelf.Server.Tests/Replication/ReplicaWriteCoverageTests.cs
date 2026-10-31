using System.Reflection;
using ByteShelf.Server.Replication.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ByteShelf.Server.Tests.Replication
{
    /// <summary>
    /// A replica refuses every action that is not a read, but for those listed here: none of them
    /// changes anything a replica holds, so a new write is refused there until it is added on purpose.
    /// </summary>
    public class ReplicaWriteCoverageTests
    {
        private static readonly string[] AllowedOnReplica =
        {
            "EntriesController.CreateDownloadLink",
            "ReplicationController.Promote",
        };

        [Fact]
        public void EveryActionAllowedOnAReplica_IsListed()
        {
            List<string> allowed = new List<string>();

            foreach (Type controller in typeof(Program).Assembly.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract))
            {
                foreach (MethodInfo action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    bool writes = action.GetCustomAttributes<HttpMethodAttribute>().Any(attribute => attribute.HttpMethods.Any(method => method is not "GET" and not "HEAD"));
                    if (writes && action.GetCustomAttribute<AllowedOnReplicaAttribute>() != null)
                    {
                        allowed.Add($"{controller.Name}.{action.Name}");
                    }
                }
            }

            Assert.Equal(AllowedOnReplica.Order(StringComparer.Ordinal), allowed.Order(StringComparer.Ordinal));
        }
    }
}
