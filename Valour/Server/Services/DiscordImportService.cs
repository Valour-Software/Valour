using System.Text.Json;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Server.Models;

namespace Valour.Server.Services;

public class DiscordImportService
{
    private readonly HttpClient _http;
    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<DiscordImportService> _logger;

    public DiscordImportService(
        HttpClient http,
        ValourDb db,
        CoreHubService coreHub,
        ILogger<DiscordImportService> logger)
    {
        _http = http;
        _db = db;
        _coreHub = coreHub;
        _logger = logger;
    }

    /// <summary>
    /// Extracts the template code from a full URL or plain code string.
    /// </summary>
    private static string ParseTemplateCode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        input = input.Trim();

        var idx = input.IndexOf("discord.new/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return input[(idx + "discord.new/".Length)..].Trim().TrimEnd('/');

        return input;
    }

    /// <summary>
    /// Fetches the template from the Discord API and returns the serialized_source_guild element.
    /// </summary>
    private async Task<TaskResult<JsonElement>> FetchTemplateAsync(string code)
    {
        var url = $"https://discord.com/api/v10/guilds/templates/{code}";
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reach Discord API for template {Code}", code);
            return TaskResult<JsonElement>.FromFailure("Failed to reach Discord API.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return TaskResult<JsonElement>.FromFailure(
                $"Discord returned {(int)response.StatusCode}. The template code may be invalid.");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("serialized_source_guild", out var guild))
        {
            return TaskResult<JsonElement>.FromFailure("Discord template response missing guild data.");
        }

        // Clone so it survives disposal of the JsonDocument
        return TaskResult<JsonElement>.FromData(guild.Clone());
    }

    /// <summary>
    /// Maps Discord permission bitfield to Valour permission longs + IsAdmin flag.
    /// </summary>
    internal static (long planet, long chat, long category, long voice, bool isAdmin) MapRolePermissions(long discordPerms)
    {
        long planet = 0;
        long chat = 0;
        long category = 0;
        long voice = 0;
        bool isAdmin = false;

        // Planet permissions
        if ((discordPerms & (1L << 0)) != 0)  planet |= 0x02;    // CREATE_INSTANT_INVITE → Invite
        if ((discordPerms & (1L << 1)) != 0)  planet |= 0x10;    // KICK_MEMBERS → Kick
        if ((discordPerms & (1L << 2)) != 0)  planet |= 0x20;    // BAN_MEMBERS → Ban
        if ((discordPerms & (1L << 3)) != 0)  isAdmin = true;     // ADMINISTRATOR → IsAdmin
        if ((discordPerms & (1L << 4)) != 0)  planet |= 0x40;    // MANAGE_CHANNELS → CreateChannels
        if ((discordPerms & (1L << 5)) != 0)  planet |= 0x08;    // MANAGE_GUILD → Manage
        if ((discordPerms & (1L << 17)) != 0) planet |= 0x1000;  // MENTION_EVERYONE → MentionAll
        if ((discordPerms & (1L << 28)) != 0) planet |= 0x80;    // MANAGE_ROLES → ManageRoles

        // Chat permissions
        if ((discordPerms & (1L << 10)) != 0) chat |= 0x01;  // VIEW_CHANNEL → View
        if ((discordPerms & (1L << 11)) != 0) chat |= 0x04;  // SEND_MESSAGES → PostMessages
        if ((discordPerms & (1L << 13)) != 0) chat |= 0x80;  // MANAGE_MESSAGES → ManageMessages
        if ((discordPerms & (1L << 14)) != 0) chat |= 0x20;  // EMBED_LINKS → Embed
        if ((discordPerms & (1L << 15)) != 0) chat |= 0x40;  // ATTACH_FILES → AttachContent
        if ((discordPerms & (1L << 16)) != 0) chat |= 0x02;  // READ_MESSAGE_HISTORY → ViewMessages
        if ((discordPerms & (1L << 6)) != 0)  chat |= 0x200; // ADD_REACTIONS → UseReactions

        // Category permissions
        if ((discordPerms & (1L << 10)) != 0) category |= 0x01; // VIEW_CHANNEL → View
        if ((discordPerms & (1L << 4)) != 0)  category |= 0x08; // MANAGE_CHANNELS → ManageCategory
        if ((discordPerms & (1L << 28)) != 0) category |= 0x10; // MANAGE_ROLES → ManagePermissions

        // Voice permissions
        if ((discordPerms & (1L << 10)) != 0) voice |= 0x01; // VIEW_CHANNEL → View
        if ((discordPerms & (1L << 20)) != 0) voice |= 0x02; // CONNECT → Join
        if ((discordPerms & (1L << 21)) != 0) voice |= 0x04; // SPEAK → Speak
        if ((discordPerms & (1L << 4)) != 0)  voice |= 0x08; // MANAGE_CHANNELS → ManageChannel
        if ((discordPerms & (1L << 28)) != 0) voice |= 0x10; // MANAGE_ROLES → ManagePermissions

        return (planet, chat, category, voice, isAdmin);
    }

    /// <summary>
    /// Maps Discord permission overwrites (allow/deny) to Valour PermissionsNode code/mask
    /// for a given channel type.
    /// </summary>
    internal static (long code, long mask) MapOverwrite(long allow, long deny, ChannelTypeEnum channelType)
    {
        long code = 0;
        long mask = 0;

        switch (channelType)
        {
            case ChannelTypeEnum.PlanetChat:
                MapChatBit(allow, deny, 1L << 10, 0x01, ref code, ref mask); // VIEW_CHANNEL → View
                MapChatBit(allow, deny, 1L << 11, 0x04, ref code, ref mask); // SEND_MESSAGES → PostMessages
                MapChatBit(allow, deny, 1L << 13, 0x80, ref code, ref mask); // MANAGE_MESSAGES → ManageMessages
                MapChatBit(allow, deny, 1L << 14, 0x20, ref code, ref mask); // EMBED_LINKS → Embed
                MapChatBit(allow, deny, 1L << 15, 0x40, ref code, ref mask); // ATTACH_FILES → AttachContent
                MapChatBit(allow, deny, 1L << 16, 0x02, ref code, ref mask); // READ_MESSAGE_HISTORY → ViewMessages
                MapChatBit(allow, deny, 1L << 6,  0x200, ref code, ref mask); // ADD_REACTIONS → UseReactions
                break;

            case ChannelTypeEnum.PlanetCategory:
                MapChatBit(allow, deny, 1L << 10, 0x01, ref code, ref mask); // VIEW_CHANNEL → View
                MapChatBit(allow, deny, 1L << 4,  0x08, ref code, ref mask); // MANAGE_CHANNELS → ManageCategory
                MapChatBit(allow, deny, 1L << 28, 0x10, ref code, ref mask); // MANAGE_ROLES → ManagePermissions
                break;

            case ChannelTypeEnum.PlanetVoice:
                MapChatBit(allow, deny, 1L << 10, 0x01, ref code, ref mask); // VIEW_CHANNEL → View
                MapChatBit(allow, deny, 1L << 20, 0x02, ref code, ref mask); // CONNECT → Join
                MapChatBit(allow, deny, 1L << 21, 0x04, ref code, ref mask); // SPEAK → Speak
                MapChatBit(allow, deny, 1L << 4,  0x08, ref code, ref mask); // MANAGE_CHANNELS → ManageChannel
                MapChatBit(allow, deny, 1L << 28, 0x10, ref code, ref mask); // MANAGE_ROLES → ManagePermissions
                break;
        }

        return (code, mask);
    }

    private static void MapChatBit(long allow, long deny, long discordBit, long valourBit,
        ref long code, ref long mask)
    {
        if ((allow & discordBit) != 0)
        {
            code |= valourBit;
            mask |= valourBit;
        }
        else if ((deny & discordBit) != 0)
        {
            // bit stays 0 in code (deny), but is set in mask (explicitly defined)
            mask |= valourBit;
        }
    }

    /// <summary>
    /// Converts a Discord color integer to a hex string.
    /// </summary>
    internal static string ColorToHex(int color) =>
        color == 0 ? "#ffffff" : $"#{color:X6}";

    /// <summary>
    /// Imports a Discord template and creates a fully scaffolded planet.
    /// </summary>
    public async Task<TaskResult<Planet>> ImportAsync(string templateCodeOrUrl, User user, string nameOverride = null)
    {
        var code = ParseTemplateCode(templateCodeOrUrl);
        if (string.IsNullOrWhiteSpace(code))
            return TaskResult<Planet>.FromFailure("Template code or URL is required.");

        var fetchResult = await FetchTemplateAsync(code);
        if (!fetchResult.Success)
            return TaskResult<Planet>.FromFailure(fetchResult.Message);

        var guild = fetchResult.Data;

        // Determine planet name
        var guildName = guild.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "Imported Planet";
        var planetName = !string.IsNullOrWhiteSpace(nameOverride) ? nameOverride : guildName;
        if (planetName != null && planetName.Length > 32)
            planetName = planetName[..32];

        await using var tran = await _db.Database.BeginTransactionAsync();

        Valour.Database.Planet planet;
        try
        {
            // Create the planet
            planet = new Valour.Database.Planet
            {
                Id = IdManager.Generate(),
                OwnerId = user.Id,
                Name = planetName,
                Description = "Imported from Discord template",
            };

            // ── Roles ──

            var discordRoles = guild.TryGetProperty("roles", out var rolesProp)
                ? rolesProp.EnumerateArray().ToList()
                : new List<JsonElement>();

            // Sort by position descending so higher position (less authority) comes first.
            // Discord: higher position = more authority. Valour: lower position = more authority.
            // We assign Valour position in ascending order from 0.
            discordRoles = discordRoles
                .OrderByDescending(r => r.GetProperty("position").GetInt32())
                .ToList();

            var dbRoles = new List<Valour.Database.PlanetRole>();
            // Discord placeholder id → Valour role id
            var roleIdMap = new Dictionary<long, long>();
            // Discord placeholder id → FlagBitIndex
            var roleFlagMap = new Dictionary<long, int>();

            int flagBitIndex = 1; // 0 is for default
            uint rolePosition = 0;

            foreach (var dr in discordRoles)
            {
                var discordId = dr.GetProperty("id").GetInt64();
                var permString = dr.TryGetProperty("permissions", out var permProp)
                    ? permProp.GetString() ?? "0"
                    : "0";
                long.TryParse(permString, out var discordPerms);
                var (pPerm, cPerm, catPerm, vPerm, isAdminRole) = MapRolePermissions(discordPerms);

                var roleName = dr.TryGetProperty("name", out var rn) ? rn.GetString() : "Role";
                var colorInt = dr.TryGetProperty("color", out var cp) ? cp.GetInt32() : 0;
                var mentionable = dr.TryGetProperty("mentionable", out var mp) && mp.GetBoolean();

                var isDefault = discordId == 0;

                var dbRole = new Valour.Database.PlanetRole
                {
                    Planet = planet,
                    Id = IdManager.Generate(),
                    Position = isDefault ? (uint)int.MaxValue : rolePosition++,
                    IsDefault = isDefault,
                    FlagBitIndex = isDefault ? 0 : flagBitIndex,
                    Name = isDefault ? "everyone" : (roleName ?? "Role"),
                    IsAdmin = isAdminRole,
                    Permissions = isDefault ? PlanetPermissions.Default : pPerm,
                    ChatPermissions = isDefault ? ChatChannelPermissions.Default : cPerm,
                    CategoryPermissions = isDefault ? CategoryPermissions.Default : catPerm,
                    VoicePermissions = isDefault ? VoiceChannelPermissions.Default : vPerm,
                    Color = ColorToHex(colorInt),
                    AnyoneCanMention = mentionable,
                };

                dbRoles.Add(dbRole);
                roleIdMap[discordId] = dbRole.Id;
                roleFlagMap[discordId] = dbRole.FlagBitIndex;

                if (!isDefault)
                    flagBitIndex++;
            }

            planet.Roles = dbRoles;

            // ── Channels ──

            var discordChannels = guild.TryGetProperty("channels", out var chProp)
                ? chProp.EnumerateArray().ToList()
                : new List<JsonElement>();

            var dbChannels = new List<Valour.Database.Channel>();
            var dbPermNodes = new List<Valour.Database.PermissionsNode>();

            // Discord id → Valour channel database object
            var channelMap = new Dictionary<long, Valour.Database.Channel>();
            // Discord id → ChannelPosition (for parent positioning)
            var channelPositionMap = new Dictionary<long, ChannelPosition>();

            // Pass 1: Categories (Discord type 4)
            var categories = discordChannels
                .Where(c => c.GetProperty("type").GetInt32() == 4)
                .OrderBy(c => c.GetProperty("position").GetInt32())
                .ToList();

            uint catIndex = 1;
            foreach (var cat in categories)
            {
                var discordId = cat.GetProperty("id").GetInt64();
                var catName = cat.TryGetProperty("name", out var cn) ? cn.GetString() : "Category";

                var position = new ChannelPosition();
                position = position.Append(catIndex);

                var dbCat = new Valour.Database.Channel
                {
                    Planet = planet,
                    Id = IdManager.Generate(),
                    Name = catName ?? "Category",
                    Description = null,
                    ParentId = null,
                    RawPosition = position.RawPosition,
                    ChannelType = ChannelTypeEnum.PlanetCategory,
                };

                dbChannels.Add(dbCat);
                channelMap[discordId] = dbCat;
                channelPositionMap[discordId] = position;

                // Permission overwrites for category
                BuildPermissionNodes(cat, dbCat, planet, ChannelTypeEnum.PlanetCategory,
                    roleIdMap, dbPermNodes);

                catIndex++;
            }

            // Pass 2: Child channels
            // Discord types: 0 (text), 5 (announcement), 15 (forum) → PlanetChat
            //                2 (voice), 13 (stage) → PlanetVoice
            var childChannels = discordChannels
                .Where(c =>
                {
                    var t = c.GetProperty("type").GetInt32();
                    return t != 4; // everything except categories
                })
                .OrderBy(c => c.GetProperty("position").GetInt32())
                .ToList();

            // Track child index per parent for positioning
            var childCounters = new Dictionary<long, uint>();
            // For orphan channels (no parent), auto-create a category
            Valour.Database.Channel orphanCategory = null;
            ChannelPosition orphanCatPosition = default;
            bool firstChatSet = false;

            foreach (var ch in childChannels)
            {
                var discordId = ch.GetProperty("id").GetInt64();
                var discordType = ch.GetProperty("type").GetInt32();
                var chName = ch.TryGetProperty("name", out var cn) ? cn.GetString() : "channel";

                var parentId = ch.TryGetProperty("parent_id", out var pp) && pp.ValueKind != JsonValueKind.Null
                    ? pp.GetInt64()
                    : (long?)null;

                ChannelTypeEnum valourType;
                if (discordType is 2 or 13)
                    valourType = ChannelTypeEnum.PlanetVoice;
                else
                    valourType = ChannelTypeEnum.PlanetChat;

                // Determine parent
                Valour.Database.Channel parentChannel = null;
                ChannelPosition parentPosition;

                if (parentId.HasValue && channelMap.TryGetValue(parentId.Value, out parentChannel))
                {
                    parentPosition = channelPositionMap[parentId.Value];
                }
                else
                {
                    // Orphan channel — create auto category if needed
                    if (orphanCategory is null)
                    {
                        orphanCatPosition = new ChannelPosition();
                        orphanCatPosition = orphanCatPosition.Append(catIndex++);

                        orphanCategory = new Valour.Database.Channel
                        {
                            Planet = planet,
                            Id = IdManager.Generate(),
                            Name = "Channels",
                            Description = null,
                            ParentId = null,
                            RawPosition = orphanCatPosition.RawPosition,
                            ChannelType = ChannelTypeEnum.PlanetCategory,
                        };
                        dbChannels.Add(orphanCategory);
                    }

                    parentChannel = orphanCategory;
                    parentPosition = orphanCatPosition;
                    parentId = null; // use the orphan category's discord id mapping below
                }

                // Track child index
                var parentKey = parentChannel.Id;
                if (!childCounters.TryGetValue(parentKey, out var childIdx))
                    childIdx = 0;

                childIdx++;
                childCounters[parentKey] = childIdx;

                var childPosition = parentPosition.Append(childIdx);

                var dbChannel = new Valour.Database.Channel
                {
                    Planet = planet,
                    Parent = parentChannel,
                    Id = IdManager.Generate(),
                    Name = chName ?? "channel",
                    Description = null,
                    RawPosition = childPosition.RawPosition,
                    ChannelType = valourType,
                    IsDefault = !firstChatSet && valourType == ChannelTypeEnum.PlanetChat,
                };

                if (dbChannel.IsDefault)
                    firstChatSet = true;

                dbChannels.Add(dbChannel);
                channelMap[discordId] = dbChannel;

                // Permission overwrites
                BuildPermissionNodes(ch, dbChannel, planet, valourType,
                    roleIdMap, dbPermNodes);
            }

            planet.Channels = dbChannels;

            // If no chat channel was created, create a default one
            if (!firstChatSet)
            {
                // Ensure we have at least one category
                if (orphanCategory is null && !categories.Any())
                {
                    orphanCatPosition = new ChannelPosition();
                    orphanCatPosition = orphanCatPosition.Append(catIndex++);

                    orphanCategory = new Valour.Database.Channel
                    {
                        Planet = planet,
                        Id = IdManager.Generate(),
                        Name = "General",
                        Description = null,
                        ParentId = null,
                        RawPosition = orphanCatPosition.RawPosition,
                        ChannelType = ChannelTypeEnum.PlanetCategory,
                    };
                    planet.Channels.Add(orphanCategory);
                }

                var defaultParent = orphanCategory ?? dbChannels.First(c => c.ChannelType == ChannelTypeEnum.PlanetCategory);
                var defaultParentPosition = new ChannelPosition(defaultParent.RawPosition);
                var defChildIdx = childCounters.GetValueOrDefault(defaultParent.Id, 0u) + 1;

                var defaultChat = new Valour.Database.Channel
                {
                    Planet = planet,
                    Parent = defaultParent,
                    Id = IdManager.Generate(),
                    Name = "general",
                    Description = "General chat channel",
                    RawPosition = defaultParentPosition.Append(defChildIdx).RawPosition,
                    ChannelType = ChannelTypeEnum.PlanetChat,
                    IsDefault = true,
                };
                planet.Channels.Add(defaultChat);
            }

            // ── Owner member ──
            var member = new Valour.Database.PlanetMember
            {
                Planet = planet,
                Id = IdManager.Generate(),
                Nickname = user.Name,
                UserId = user.Id,
                RoleMembership = new PlanetRoleMembership(0x01) // default role flag
            };

            planet.Members = new List<Valour.Database.PlanetMember> { member };

            // ── Save ──
            _db.Planets.Add(planet);

            if (dbPermNodes.Count > 0)
                _db.PermissionsNodes.AddRange(dbPermNodes);

            await _db.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to import Discord template");
            await tran.RollbackAsync();
            return TaskResult<Planet>.FromFailure("Failed to import Discord template.");
        }

        var returnModel = planet.ToModel();
        _coreHub.NotifyPlanetChange(returnModel);

        return new TaskResult<Planet>(true, "Planet imported successfully", returnModel);
    }

    /// <summary>
    /// Builds PermissionsNode entries for a channel's permission_overwrites.
    /// </summary>
    private void BuildPermissionNodes(
        JsonElement discordChannel,
        Valour.Database.Channel dbChannel,
        Valour.Database.Planet planet,
        ChannelTypeEnum channelType,
        Dictionary<long, long> roleIdMap,
        List<Valour.Database.PermissionsNode> nodes)
    {
        if (!discordChannel.TryGetProperty("permission_overwrites", out var overwrites))
            return;

        foreach (var ow in overwrites.EnumerateArray())
        {
            // Only process role overwrites (type 0), skip member overwrites (type 1)
            var owType = ow.TryGetProperty("type", out var tp) ? tp.GetInt32() : -1;
            if (owType != 0)
                continue;

            var discordRoleId = ow.GetProperty("id").GetInt64();
            if (!roleIdMap.TryGetValue(discordRoleId, out var valourRoleId))
                continue;

            var allowStr = ow.TryGetProperty("allow", out var ap) ? ap.GetString() ?? "0" : "0";
            var denyStr = ow.TryGetProperty("deny", out var dp) ? dp.GetString() ?? "0" : "0";
            long.TryParse(allowStr, out var allow);
            long.TryParse(denyStr, out var deny);

            if (allow == 0 && deny == 0)
                continue;

            var (code, mask) = MapOverwrite(allow, deny, channelType);

            if (mask == 0)
                continue;

            nodes.Add(new Valour.Database.PermissionsNode
            {
                Id = IdManager.Generate(),
                PlanetId = planet.Id,
                RoleId = valourRoleId,
                TargetId = dbChannel.Id,
                TargetType = channelType,
                Code = code,
                Mask = mask,
            });
        }
    }
}
