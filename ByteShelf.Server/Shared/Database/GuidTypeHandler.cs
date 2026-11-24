using System.Data;
using System.Text.Json;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>Binds and reads <see cref="Guid"/> as lowercase hyphenated text, the form every id column holds.</summary>
    public class GuidTypeHandler : TypeHandler<Guid>
    {
        /// <summary>The ids as one JSON array, as the database stores each, for a query to read with <c>json_each</c> rather than take a parameter per id, of which a long list would run out.</summary>
        public static string ToJsonArray(IEnumerable<Guid> ids)
        {
            return JsonSerializer.Serialize(ids.Select(Stored));
        }

        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.Value = Stored(value);
            parameter.DbType = DbType.String;
        }

        public override Guid Parse(object value)
        {
            return Guid.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        private static string Stored(Guid id)
        {
            return id.ToString("D");
        }
    }
}
