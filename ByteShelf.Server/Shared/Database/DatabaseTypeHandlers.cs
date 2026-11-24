using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>Every type handler the data layer needs, registered once for the process by <see cref="DatabaseFile"/>.</summary>
    internal static class DatabaseTypeHandlers
    {
        public static void RegisterAll()
        {
            SQLitePCL.Batteries_V2.Init();

            TypeHandlerRegistry.AddTypeHandler(new GuidTypeHandler());
            TypeHandlerRegistry.AddTypeHandler(new DateTimeOffsetTypeHandler());
            TypeHandlerRegistry.AddTypeHandler(new DateTimeTypeHandler());
        }
    }
}
