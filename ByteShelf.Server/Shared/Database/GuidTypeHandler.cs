using System.Data;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>Binds and reads <see cref="Guid"/> as lowercase hyphenated text, the form every id column holds.</summary>
    public class GuidTypeHandler : TypeHandler<Guid>
    {
        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.Value = value.ToString("D");
            parameter.DbType = DbType.String;
        }

        public override Guid Parse(object value)
        {
            return Guid.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }
}
