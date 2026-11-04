using System.Text.Json;
using ByteShelf.Server.Shared.Json;

namespace ByteShelf.Server.ChangeLog.Models
{
    /// <summary>The one way an entity's state is written into <c>change_log.payload</c> and read back.</summary>
    public static class ChangePayload
    {
        public static string Serialize<TEntity>(TEntity entity)
        {
            return JsonSerializer.Serialize(entity, JsonDefaults.CamelCase);
        }

        public static TEntity Deserialize<TEntity>(string payload)
        {
            return JsonSerializer.Deserialize<TEntity>(payload, JsonDefaults.CamelCase)
                ?? throw new InvalidOperationException($"A change-log payload read as null for {typeof(TEntity).Name}.");
        }
    }
}
