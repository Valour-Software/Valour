using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Server;

public class DiscordImportServiceTests
{
    // Discord permission bits
    private const long CreateInstantInvite = 1L << 0;
    private const long KickMembers = 1L << 1;
    private const long BanMembers = 1L << 2;
    private const long Administrator = 1L << 3;
    private const long ManageChannels = 1L << 4;
    private const long ManageGuild = 1L << 5;
    private const long AddReactions = 1L << 6;
    private const long ViewChannel = 1L << 10;
    private const long SendMessages = 1L << 11;
    private const long ManageMessages = 1L << 13;
    private const long AttachFiles = 1L << 15;
    private const long Connect = 1L << 20;
    private const long Speak = 1L << 21;

    [Theory]
    [InlineData("general", "general")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("", "channel")]
    [InlineData("   ", "channel")]
    [InlineData(null, "channel")]
    public void ToChannelName_TrimsAndFallsBack(string? input, string expected)
    {
        Assert.Equal(expected, DiscordImportService.ToChannelName(input, "channel"));
    }

    [Fact]
    public void ToChannelName_CutsDiscordLengthNamesToChannelLimit()
    {
        // Discord allows 100 characters; Valour channel names are limited to 32
        var name = DiscordImportService.ToChannelName(new string('a', 100), "channel");
        Assert.Equal(32, name.Length);
    }

    [Fact]
    public void CutToLength_DoesNotSplitSurrogatePairs()
    {
        // The emoji is a surrogate pair that straddles the limit
        var name = DiscordImportService.CutToLength(new string('a', 31) + "\U0001F4E2" + "news", 32);
        Assert.Equal(new string('a', 31), name);
    }

    [Fact]
    public void MapRolePermissions_AdministratorSetsAdminFlag()
    {
        var (_, _, _, _, isAdmin) = DiscordImportService.MapRolePermissions(Administrator);
        Assert.True(isAdmin);
    }

    [Fact]
    public void MapRolePermissions_NoAdminWithoutBit()
    {
        var (_, _, _, _, isAdmin) = DiscordImportService.MapRolePermissions(
            KickMembers | BanMembers | ManageGuild);
        Assert.False(isAdmin);
    }

    [Fact]
    public void MapRolePermissions_MapsModerationBits()
    {
        var (planet, _, _, _, _) = DiscordImportService.MapRolePermissions(
            CreateInstantInvite | KickMembers | BanMembers | ManageGuild);

        Assert.Equal(0x02, planet & 0x02); // Invite
        Assert.Equal(0x10, planet & 0x10); // Kick
        Assert.Equal(0x20, planet & 0x20); // Ban
        Assert.Equal(0x08, planet & 0x08); // Manage
    }

    [Fact]
    public void MapRolePermissions_MapsChatBits()
    {
        var (_, chat, _, _, _) = DiscordImportService.MapRolePermissions(
            ViewChannel | SendMessages | AttachFiles | AddReactions);

        Assert.Equal(0x01, chat & 0x01);   // View
        Assert.Equal(0x04, chat & 0x04);   // PostMessages
        Assert.Equal(0x40, chat & 0x40);   // AttachContent
        Assert.Equal(0x200, chat & 0x200); // UseReactions
        Assert.Equal(0, chat & 0x80);      // ManageMessages not granted
    }

    [Fact]
    public void MapRolePermissions_MapsVoiceBits()
    {
        var (_, _, _, voice, _) = DiscordImportService.MapRolePermissions(
            ViewChannel | Connect | Speak);

        Assert.Equal(0x01, voice & 0x01); // View
        Assert.Equal(0x02, voice & 0x02); // Join
        Assert.Equal(0x04, voice & 0x04); // Speak
    }

    [Fact]
    public void MapRolePermissions_ZeroInZeroOut()
    {
        var (planet, chat, category, voice, isAdmin) = DiscordImportService.MapRolePermissions(0);

        Assert.Equal(0, planet);
        Assert.Equal(0, chat);
        Assert.Equal(0, category);
        Assert.Equal(0, voice);
        Assert.False(isAdmin);
    }

    [Fact]
    public void MapOverwrite_AllowSetsCodeAndMask()
    {
        var (code, mask) = DiscordImportService.MapOverwrite(
            allow: ViewChannel | SendMessages, deny: 0, ChannelTypeEnum.PlanetChat);

        Assert.Equal(0x01 | 0x04, code);
        Assert.Equal(0x01 | 0x04, mask);
    }

    [Fact]
    public void MapOverwrite_DenySetsMaskOnly()
    {
        var (code, mask) = DiscordImportService.MapOverwrite(
            allow: 0, deny: SendMessages, ChannelTypeEnum.PlanetChat);

        Assert.Equal(0, code & 0x04);    // denied: bit off in code
        Assert.Equal(0x04, mask & 0x04); // but explicitly defined in mask
    }

    [Fact]
    public void MapOverwrite_MixedAllowDeny()
    {
        var (code, mask) = DiscordImportService.MapOverwrite(
            allow: ViewChannel, deny: SendMessages | ManageMessages, ChannelTypeEnum.PlanetChat);

        Assert.Equal(0x01, code);                // only View allowed
        Assert.Equal(0x01 | 0x04 | 0x80, mask);  // all three explicitly defined
    }

    [Fact]
    public void MapOverwrite_UnmappedBitsIgnored()
    {
        // MANAGE_GUILD has no chat-channel meaning; overwrite should be empty
        var (code, mask) = DiscordImportService.MapOverwrite(
            allow: ManageGuild, deny: 0, ChannelTypeEnum.PlanetChat);

        Assert.Equal(0, code);
        Assert.Equal(0, mask);
    }

    [Fact]
    public void MapOverwrite_VoiceChannelUsesVoiceBits()
    {
        var (code, mask) = DiscordImportService.MapOverwrite(
            allow: Connect | Speak, deny: 0, ChannelTypeEnum.PlanetVoice);

        Assert.Equal(0x02 | 0x04, code); // Join | Speak
        Assert.Equal(0x02 | 0x04, mask);
    }

    [Theory]
    [InlineData(0, "#ffffff")]
    [InlineData(0x5865F2, "#5865F2")]
    [InlineData(0xFF0000, "#FF0000")]
    public void ColorToHex_Converts(int color, string expected)
    {
        Assert.Equal(expected, DiscordImportService.ColorToHex(color));
    }
}
