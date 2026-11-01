using System.Reflection;
using ByteShelfClient;

namespace ByteShelf.Cli.Tests.TestUtilities
{
    /// <summary>
    /// A shelf that passes every call on to another one, except those a test answers itself: to hold
    /// a download open, or to answer what no real shelf would.
    /// </summary>
    public class ShelfStandIn : DispatchProxy
    {
        private IShelfClient _inner = null!;
        private Func<MethodInfo, object?[]?, object?> _answer = null!;

        /// <param name="answer">The answer to a call, or null to pass it on to <paramref name="inner"/>.</param>
        public static IShelfClient Over(IShelfClient inner, Func<MethodInfo, object?[]?, object?> answer)
        {
            IShelfClient standIn = Create<IShelfClient, ShelfStandIn>();
            ShelfStandIn proxy = (ShelfStandIn)(object)standIn;
            proxy._inner = inner;
            proxy._answer = answer;
            return standIn;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return _answer(targetMethod!, args) ?? targetMethod!.Invoke(_inner, args);
        }
    }
}
