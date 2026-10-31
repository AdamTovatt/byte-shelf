namespace ByteShelfCommon.Tests
{
    /// <summary>The rules for users: <see cref="UserFields"/>, <see cref="DisplayText"/>, <see cref="PasswordPolicy"/> and <see cref="UserRoles"/>.</summary>
    public class UserRulesTests
    {
        [Fact]
        public void ValidateEmail_LongestAllowed_IsAccepted()
        {
            UserFields.ValidateEmail("a@" + new string('b', UserFields.MaxEmailLength - 2));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("no-at-sign")]
        public void ValidateEmail_NotAnEmail_Throws(string email)
        {
            Assert.Throws<ArgumentException>(() => UserFields.ValidateEmail(email));
        }

        [Fact]
        public void ValidateEmail_OneTooLong_Throws()
        {
            Assert.Throws<ArgumentException>(() => UserFields.ValidateEmail("a@" + new string('b', UserFields.MaxEmailLength - 1)));
        }

        [Fact]
        public void NormalizeDisplayName_Padded_AnswersItTrimmed()
        {
            Assert.Equal("Ada", UserFields.NormalizeDisplayName("  Ada "));
        }

        [Fact]
        public void DisplayText_LongestOnceTrimmed_IsAccepted()
        {
            Assert.Equal(new string('a', 10), DisplayText.Validate(" " + new string('a', 10) + " ", 10, "A name"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        [InlineData("aaaaaaaaaaa")]
        [InlineData("a\tb")]
        [InlineData("a\x0085b")]
        public void DisplayText_BreaksTheRule_ThrowsNamingTheSubject(string? value)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => DisplayText.Validate(value, 10, "A name"));

            Assert.StartsWith("A name ", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(PasswordPolicy.MinimumLength)]
        [InlineData(PasswordPolicy.MaximumLength)]
        public void PasswordPolicy_LengthAtALimit_IsAccepted(int length)
        {
            PasswordPolicy.Validate(new string('p', length));
        }

        [Theory]
        [InlineData(PasswordPolicy.MinimumLength - 1)]
        [InlineData(PasswordPolicy.MaximumLength + 1)]
        public void PasswordPolicy_LengthPastALimit_Throws(int length)
        {
            Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate(new string('p', length)));
        }

        [Fact]
        public void PasswordPolicy_OnlyWhitespace_Throws()
        {
            Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate(new string(' ', PasswordPolicy.MinimumLength)));
        }

        [Fact]
        public void UserRoles_Normalize_DropsRepeats()
        {
            Assert.Equal(new[] { UserRoles.Admin }, UserRoles.Normalize(new[] { UserRoles.Admin, UserRoles.Admin }));
        }

        [Fact]
        public void UserRoles_Normalize_NoneIsEmpty()
        {
            Assert.Empty(UserRoles.Normalize(null));
        }

        [Theory]
        [InlineData("write")]
        [InlineData("Admin")]
        public void UserRoles_Normalize_UnknownRole_Throws(string role)
        {
            Assert.Throws<ArgumentException>(() => UserRoles.Normalize(new[] { role }));
        }
    }
}
