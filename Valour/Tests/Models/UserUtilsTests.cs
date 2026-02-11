using Valour.Shared.Models;

namespace Valour.Tests.Models;

public class UserUtilsTests
{
    // ──────────────────────────────────────────────
    // SanitizeEmail
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeEmail_ReturnsNull_ForNullOrWhitespace(string input)
    {
        Assert.Null(UserUtils.SanitizeEmail(input));
    }

    [Fact]
    public void SanitizeEmail_TrimsAndLowercases()
    {
        Assert.Equal("user@example.com", UserUtils.SanitizeEmail("  User@Example.COM  "));
    }

    [Fact]
    public void SanitizeEmail_RemovesInvisibleCharacters()
    {
        // Zero-width space (\u200B) embedded in the email
        var dirty = "user\u200B@example.com";
        Assert.Equal("user@example.com", UserUtils.SanitizeEmail(dirty));
    }

    [Fact]
    public void SanitizeEmail_RemovesZeroWidthNoBreakSpace()
    {
        var dirty = "user@example\uFEFF.com";
        Assert.Equal("user@example.com", UserUtils.SanitizeEmail(dirty));
    }

    // ──────────────────────────────────────────────
    // TestEmail – valid addresses
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("user@proton.me")]
    [InlineData("user@protonmail.com")]
    [InlineData("user@pm.me")]
    [InlineData("firstname.lastname@protonmail.com")]
    [InlineData("user+tag@proton.me")]
    public void TestEmail_AcceptsProtonMailAddresses(string email)
    {
        var result = UserUtils.TestEmail(email);
        Assert.True(result.Success, $"Expected '{email}' to be valid but got: {result.Message}");
        Assert.Equal(email, result.Data);
    }

    [Theory]
    [InlineData("user@gmail.com")]
    [InlineData("user@outlook.com")]
    [InlineData("user@yahoo.com")]
    [InlineData("user@icloud.com")]
    [InlineData("user@hotmail.com")]
    [InlineData("user@live.com")]
    public void TestEmail_AcceptsMajorProviders(string email)
    {
        var result = UserUtils.TestEmail(email);
        Assert.True(result.Success, $"Expected '{email}' to be valid but got: {result.Message}");
    }

    [Theory]
    [InlineData("user@example.co.uk")]
    [InlineData("user@example.com.au")]
    [InlineData("user@example.co.jp")]
    public void TestEmail_AcceptsCountryCodeTlds(string email)
    {
        var result = UserUtils.TestEmail(email);
        Assert.True(result.Success, $"Expected '{email}' to be valid but got: {result.Message}");
    }

    [Theory]
    [InlineData("user@example.io")]
    [InlineData("user@example.dev")]
    [InlineData("user@example.gg")]
    [InlineData("user@example.app")]
    [InlineData("user@example.xyz")]
    [InlineData("user@example.me")]
    [InlineData("user@example.org")]
    [InlineData("user@example.net")]
    public void TestEmail_AcceptsModernTlds(string email)
    {
        var result = UserUtils.TestEmail(email);
        Assert.True(result.Success, $"Expected '{email}' to be valid but got: {result.Message}");
    }

    [Theory]
    [InlineData("user+tag@gmail.com")]
    [InlineData("first.last@example.com")]
    [InlineData("user-name@example.com")]
    [InlineData("user_name@example.com")]
    public void TestEmail_AcceptsSpecialLocalParts(string email)
    {
        var result = UserUtils.TestEmail(email);
        Assert.True(result.Success, $"Expected '{email}' to be valid but got: {result.Message}");
    }

    // ──────────────────────────────────────────────
    // TestEmail – invalid addresses
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TestEmail_RejectsNullOrWhitespace(string email)
    {
        var result = UserUtils.TestEmail(email);
        Assert.False(result.Success);
    }

    [Fact]
    public void TestEmail_RejectsNoAtSign()
    {
        var result = UserUtils.TestEmail("userexample.com");
        Assert.False(result.Success);
    }

    [Fact]
    public void TestEmail_RejectsMissingTld()
    {
        var result = UserUtils.TestEmail("user@localhost");
        Assert.False(result.Success);
    }

    [Fact]
    public void TestEmail_RejectsDisplayNameTrick()
    {
        // MailAddress parses this as display name + address
        var result = UserUtils.TestEmail("\"Real Name\" <user@example.com>");
        Assert.False(result.Success);
    }

    [Fact]
    public void TestEmail_RejectsMissingLocalPart()
    {
        var result = UserUtils.TestEmail("@example.com");
        Assert.False(result.Success);
    }

    [Fact]
    public void TestEmail_RejectsMissingDomain()
    {
        var result = UserUtils.TestEmail("user@");
        Assert.False(result.Success);
    }

    [Fact]
    public void TestEmail_RejectsDoubleAt()
    {
        var result = UserUtils.TestEmail("user@@example.com");
        Assert.False(result.Success);
    }
}
