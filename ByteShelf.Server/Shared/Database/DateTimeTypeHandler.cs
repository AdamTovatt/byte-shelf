using System.Data;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>
    /// Binds and reads <see cref="DateTime"/> as <see cref="StoredInstant"/> text, always UTC. Only the
    /// types EasyReasy.Auth hands over use <see cref="DateTime"/>; everything of this server's own uses
    /// <see cref="DateTimeOffset"/>.
    /// </summary>
    public class DateTimeTypeHandler : TypeHandler<DateTime>
    {
        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            parameter.Value = StoredInstant.ToText(value);
            parameter.DbType = DbType.String;
        }

        public override DateTime Parse(object value)
        {
            return StoredInstant.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).UtcDateTime;
        }
    }
}
