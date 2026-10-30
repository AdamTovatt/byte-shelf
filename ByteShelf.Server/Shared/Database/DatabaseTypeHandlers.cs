using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>
    /// Registers every type handler the data layer needs. The application and the test fixtures both
    /// call this one list, so the tests never run against a storage format production does not use.
    /// </summary>
    public static class DatabaseTypeHandlers
    {
        private static readonly object RegistrationLock = new object();
        private static bool _registered;

        public static void RegisterAll()
        {
            lock (RegistrationLock)
            {
                if (_registered)
                {
                    return;
                }

                SQLitePCL.Batteries_V2.Init();

                TypeHandlerRegistry.AddTypeHandler(new GuidTypeHandler());
                TypeHandlerRegistry.AddTypeHandler(new DateTimeOffsetTypeHandler());
                TypeHandlerRegistry.AddTypeHandler(new DateTimeTypeHandler());

                _registered = true;
            }
        }
    }
}
