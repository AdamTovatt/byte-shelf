using System.Reflection;
using ByteShelf.Server.Plugins.Services;

namespace ByteShelf.Server.Tests.Plugins
{
    /// <summary>The exception a plugin's failure is told by.</summary>
    public class PluginFailureTests
    {
        private static readonly DllNotFoundException Cause = new DllNotFoundException("There is no libimages.so.");

        /// <summary>As of a reader with a native library in a static field.</summary>
        [Fact]
        public void CauseOf_AnInitializerThatFailedInWhatWasInvoked_IsWhatItThrew()
        {
            Assert.Same(Cause, PluginFailure.CauseOf(new TargetInvocationException(new TypeInitializationException("Images", Cause))));
        }

        [Fact]
        public void CauseOf_TypesThatCouldNotBeLoaded_IsWhyOfTheFirst()
        {
            ReflectionTypeLoadException exception = new ReflectionTypeLoadException(
                [null, null], [new TypeInitializationException("Images", Cause), new InvalidOperationException("Another.")]);

            Assert.Same(Cause, PluginFailure.CauseOf(exception));
        }

        [Fact]
        public void CauseOf_WhatWrapsNothing_IsItself()
        {
            TargetInvocationException alone = new TargetInvocationException(null);
            InvalidOperationException telling = new InvalidOperationException("It says why.", Cause);

            Assert.Same(alone, PluginFailure.CauseOf(alone));
            Assert.Same(telling, PluginFailure.CauseOf(telling));
        }
    }
}
