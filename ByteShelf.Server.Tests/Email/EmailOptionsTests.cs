using ByteShelf.Server.Email;

namespace ByteShelf.Server.Tests.Email
{
    /// <summary>Which settings the environment's values make, and the rules that hold them together.</summary>
    public sealed class EmailOptionsTests
    {
        [Fact]
        public void FromValues_NothingSet_OnlyLogs()
        {
            Assert.Same(EmailOptions.None, EmailOptions.FromValues(null, null, null, null));
        }

        [Fact]
        public void FromValues_BlankValues_CountAsUnset()
        {
            Assert.Same(EmailOptions.None, EmailOptions.FromValues("  ", "", " ", "\t"));
        }

        [Fact]
        public void FromValues_TokenAndSender_IsPostmark_EvenBesideADevMailFolder_WithValuesTrimmed()
        {
            EmailOptions options = EmailOptions.FromValues(" server-token ", " shelf@example.com ", " Shelf ", "/nowhere/mail");

            Assert.Equal(new PostmarkEmail("server-token", new MailSender("shelf@example.com", "Shelf")), options);
        }

        [Fact]
        public void FromValues_TokenWithoutASenderAddress_IsRefused()
        {
            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => EmailOptions.FromValues("server-token", null, null, null));

            Assert.StartsWith("EMAIL_FROM_ADDRESS must be set when POSTMARK_API_KEY is", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FromValues_DevMailFolderAlone_NamesTheDefaultSender()
        {
            EmailOptions options = EmailOptions.FromValues(null, null, null, "/nowhere/mail");

            Assert.Equal(new DevMailEmail("/nowhere/mail", new MailSender(EmailOptions.DevMailFromAddress, EmailOptions.DefaultFromName)), options);
        }

        [Fact]
        public void FromValues_DevMailFolderWithASender_NamesIt()
        {
            EmailOptions options = EmailOptions.FromValues(null, "shelf@example.com", "Shelf", "/nowhere/mail");

            Assert.Equal(new DevMailEmail("/nowhere/mail", new MailSender("shelf@example.com", "Shelf")), options);
        }
    }
}
