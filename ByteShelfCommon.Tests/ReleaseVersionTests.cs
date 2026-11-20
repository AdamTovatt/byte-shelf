namespace ByteShelfCommon.Tests
{
    public class ReleaseVersionTests
    {
        [Theory]
        [InlineData("2.3.0+e0364fcbc61ae1dd12d4c0a37c2e443d1fa95d29", "2.3.0")]
        [InlineData("2.3.0-beta.1+e0364fc", "2.3.0-beta.1")]
        [InlineData("2.3.0", "2.3.0")]
        public void WithoutCommit_DropsWhatFollowsThePlus(string informational, string release)
        {
            Assert.Equal(release, ReleaseVersion.WithoutCommit(informational));
        }

        [Fact]
        public void Of_ThisPackage_IsItsVersionWithoutTheCommit()
        {
            string release = ReleaseVersion.Of(typeof(ReleaseVersion).Assembly);

            Assert.Equal(typeof(ReleaseVersion).Assembly.GetName().Version!.ToString(3), release);
        }
    }
}
