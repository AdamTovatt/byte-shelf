using System.Reflection;
using System.Reflection.Emit;
using ByteShelf.Plugins;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>A plugin's server assembly written by a test, for what a compiled one cannot be without failing to build or always failing to load.</summary>
    public sealed class EmittedPlugin
    {
        private static readonly ConstructorInfo ObjectConstructor = typeof(object).GetConstructor(Type.EmptyTypes)!;

        private readonly ModuleBuilder _module;

        private EmittedPlugin(ModuleBuilder module)
        {
            _module = module;
        }

        public static void Write(string path, Action<EmittedPlugin> add)
        {
            PersistedAssemblyBuilder assembly = new PersistedAssemblyBuilder(new AssemblyName("Emitted"), typeof(object).Assembly);
            add(new EmittedPlugin(assembly.DefineDynamicModule("Emitted")));
            assembly.Save(path);
        }

        /// <summary>An entry class that registers nothing.</summary>
        public void AddEntry(string name)
        {
            DefineEntry(name).CreateType();
        }

        /// <summary>An entry class that registers nothing, compiled against <paramref name="version"/> of the contract.</summary>
        public void AddEntryAgainst(System.Version version)
        {
            PersistedAssemblyBuilder contract = new PersistedAssemblyBuilder(new AssemblyName(typeof(IByteShelfPlugin).Assembly.GetName().Name!) { Version = version }, typeof(object).Assembly);
            ModuleBuilder module = contract.DefineDynamicModule("Contract");
            TypeBuilder builder = module.DefineType(typeof(IPluginBuilder).FullName!, TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            TypeBuilder plugin = module.DefineType(typeof(IByteShelfPlugin).FullName!, TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            plugin.DefineMethod(nameof(IByteShelfPlugin.Configure), MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract, typeof(void), [builder]);
            builder.CreateType();
            plugin.CreateType();

            DefineEntry("Entry", plugin, builder).CreateType();
        }

        /// <summary>An abstract class implementing the entry interface, which is no entry.</summary>
        public void AddUnfinishedEntry()
        {
            TypeBuilder type = _module.DefineType("Unfinished", TypeAttributes.Public | TypeAttributes.Abstract, typeof(object), [typeof(IByteShelfPlugin)]);
            type.DefineMethod(nameof(IByteShelfPlugin.Configure), MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract, typeof(void), [typeof(IPluginBuilder)]);
            type.CreateType();
        }

        public void AddThrowingEntry(string message)
        {
            TypeBuilder type = DefineEntry("Throwing");
            ILGenerator body = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes).GetILGenerator();
            body.Emit(OpCodes.Ldstr, message);
            body.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            body.Emit(OpCodes.Throw);
            type.CreateType();
        }

        public void AddEntryMadeFromANumber()
        {
            TypeBuilder type = DefineEntry("FromANumber");
            ILGenerator body = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(int)]).GetILGenerator();
            body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Call, ObjectConstructor);
            body.Emit(OpCodes.Ret);
            type.CreateType();
        }

        /// <summary>A method, which nothing calls, that uses <paramref name="version"/> of <c>ByteShelfCommon</c>.</summary>
        public void AddUseOfCommon(System.Version version)
        {
            PersistedAssemblyBuilder common = new PersistedAssemblyBuilder(new AssemblyName(typeof(ShelfPath).Assembly.GetName().Name!) { Version = version }, typeof(object).Assembly);
            TypeBuilder used = common.DefineDynamicModule("Common").DefineType("Added", TypeAttributes.Public);
            used.CreateType();

            TypeBuilder type = _module.DefineType("Using", TypeAttributes.NotPublic);
            ILGenerator body = type.DefineMethod("Use", MethodAttributes.Public, typeof(void), Type.EmptyTypes).GetILGenerator();
            body.Emit(OpCodes.Ldtoken, used);
            body.Emit(OpCodes.Pop);
            body.Emit(OpCodes.Ret);
            type.CreateType();
        }

        /// <summary>An internal class deriving from one in an assembly that is nowhere.</summary>
        public void AddInternalTypeWithAMissingBase()
        {
            PersistedAssemblyBuilder missing = new PersistedAssemblyBuilder(new AssemblyName("Missing"), typeof(object).Assembly);
            TypeBuilder missingBase = missing.DefineDynamicModule("Missing").DefineType("MissingBase", TypeAttributes.Public);
            missingBase.DefineDefaultConstructor(MethodAttributes.Public);
            missingBase.CreateType();

            TypeBuilder type = _module.DefineType("Derived", TypeAttributes.NotPublic, missingBase);
            type.CreateType();
        }

        /// <summary>A controller with one action, on the route <paramref name="template"/>, or none when it is null.</summary>
        public void AddController(string name, string? template, bool isApiController)
        {
            TypeBuilder type = _module.DefineType(name, TypeAttributes.Public, typeof(ControllerBase));
            if (isApiController)
            {
                type.SetCustomAttribute(new CustomAttributeBuilder(typeof(ApiControllerAttribute).GetConstructor(Type.EmptyTypes)!, []));
            }

            MethodBuilder action = type.DefineMethod("Get", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
            action.GetILGenerator().Emit(OpCodes.Ret);
            if (template != null)
            {
                action.SetCustomAttribute(new CustomAttributeBuilder(typeof(HttpGetAttribute).GetConstructor([typeof(string)])!, [template]));
            }

            type.CreateType();
        }

        private TypeBuilder DefineEntry(string name)
        {
            return DefineEntry(name, typeof(IByteShelfPlugin), typeof(IPluginBuilder));
        }

        private TypeBuilder DefineEntry(string name, Type plugin, Type builder)
        {
            TypeBuilder type = _module.DefineType(name, TypeAttributes.Public | TypeAttributes.Class, typeof(object), [plugin]);
            MethodBuilder method = type.DefineMethod(nameof(IByteShelfPlugin.Configure), MethodAttributes.Public | MethodAttributes.Virtual, typeof(void), [builder]);
            method.GetILGenerator().Emit(OpCodes.Ret);
            return type;
        }
    }
}
