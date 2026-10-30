using System.Data;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>Binds and reads <see cref="DateTimeOffset"/> as <see cref="StoredInstant"/> text.</summary>
    public class DateTimeOffsetTypeHandler : TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.Value = StoredInstant.ToText(value);
            parameter.DbType = DbType.String;
        }

        public override DateTimeOffset Parse(object value)
        {
            return StoredInstant.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }
}
