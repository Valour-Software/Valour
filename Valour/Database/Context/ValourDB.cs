using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Valour.Config;
using Valour.Config.Configs;
using Valour.Database.Economy;
using Valour.Database.Extensions;
using Valour.Database.Themes;
using Valour.Shared.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Context;

internal class ValourDbDesignTimeContext : IDesignTimeDbContextFactory<ValourDb>
{
    public ValourDb CreateDbContext(string[] args)
    {
        // Load configs
        ConfigLoader.LoadConfigs();
        
        var optionsBuilder = new DbContextOptionsBuilder<ValourDb>();
        optionsBuilder.UseNpgsql(ValourDb.ConnectionString).UseExceptionProcessor();
        return new ValourDb(optionsBuilder.Options);
    }
}

public partial class ValourDb : DbContext, IDataProtectionKeyContext
{
    public static readonly string ConnectionString = $"Host={DbConfig.Instance.Host};Database={DbConfig.Instance.Database};Username={DbConfig.Instance.Username};Password={DbConfig.Instance.Password};SslMode=Prefer;";

    // These are the database sets we can access
    //public DbSet<ClientPlanetMessage> Messages { get; set; }
    
    /// <summary>
    /// Table for messages
    /// </summary>
    public DbSet<Message> Messages { get; set; }
    
    /// <summary>
    /// Table for Valour users
    /// </summary>
    public DbSet<User> Users { get; set; }
    
    /// <summary>
    /// Table for user subscriptions
    /// </summary>
    public DbSet<UserSubscription> UserSubscriptions { get; set; }

    /// <summary>
    /// Table for Valour user profiles
    /// </summary>
    public DbSet<UserProfile> UserProfiles { get; set; }

    /// <summary>
    /// Table for Valour user preferences
    /// </summary>
    public DbSet<UserPreferences> UserPreferences { get; set; }
  
    /// <summary>
    /// Table for Valour user friends
    /// </summary>
    public DbSet<UserFriend> UserFriends { get; set; }

    /// <summary>
    /// Table for Valour user blocks
    /// </summary>
    public DbSet<UserBlock> UserBlocks { get; set; }
    
    /// <summary>
    /// Table for user Tenor favorites
    /// </summary>
    public DbSet<TenorFavorite> TenorFavorites { get; set; }

    // USER LOGIN AND PERMISSION STUFF //

    /// <summary>
    /// Table for password and login information
    /// </summary>
    public DbSet<Credential> Credentials { get; set; }

    /// <summary>
    /// Table for multi-auth information
    /// </summary>
    public DbSet<MultiAuth> MultiAuths { get; set; }

    /// <summary>
    /// Table for email information
    /// </summary>
    public DbSet<UserPrivateInfo> PrivateInfos { get; set; }

    /// <summary>
    /// Table for blocked email addresses and hosts
    /// </summary>
    public DbSet<BlockedUserEmail> BlockedUserEmails { get; set; }

    /// <summary>
    /// Table for authentication tokens
    /// </summary>
    public DbSet<AuthToken> AuthTokens { get; set; }

    /// <summary>
    /// Table for email confirmation codes
    /// </summary>
    public DbSet<EmailConfirmCode> EmailConfirmCodes { get; set; }

    /// <summary>
    /// Table for planet definitions
    /// </summary>
    public DbSet<Planet> Planets { get; set; }

    /// <summary>
    /// Table for all planet membership
    /// </summary>
    public DbSet<PlanetMember> PlanetMembers { get; set; }

    /// <summary>
    /// Table for all channels
    /// </summary>
    public DbSet<Channel> Channels { get; set; }
    
    /// <summary>
    /// Table for all channel members (not to be confused with planet members)
    /// </summary>
    public DbSet<ChannelMember> ChannelMembers { get; set; }

    /// <summary>
    /// Table for all banned members
    /// </summary>
    public DbSet<PlanetBan> PlanetBans { get; set; }

    /// <summary>
    /// Table for planet invites
    /// </summary>
    public DbSet<PlanetInvite> PlanetInvites { get; set; }

    /// <summary>
    /// Table for automod triggers
    /// </summary>
    public DbSet<AutomodTrigger> AutomodTriggers { get; set; }

    /// <summary>
    /// Table for automod actions
    /// </summary>
    public DbSet<AutomodAction> AutomodActions { get; set; }

    /// <summary>
    /// Table for automod trigger logs
    /// </summary>
    public DbSet<AutomodLog> AutomodLogs { get; set; }

    /// <summary>
    /// Table for moderation audit logs
    /// </summary>
    public DbSet<ModerationAuditLog> ModerationAuditLogs { get; set; }

    /// <summary>
    /// Table for planet invites
    /// </summary>
    public DbSet<StatObject> Stats { get; set; }

    /// <summary>
    /// Table for referrals
    /// </summary>
    public DbSet<Referral> Referrals { get; set; }

    /// <summary>
    /// Table for recoveries
    /// </summary>
    public DbSet<PasswordRecovery> PasswordRecoveries { get; set; }

    /// <summary>
    /// Table for notification subscriptions
    /// </summary>
    public DbSet<PushNotificationSubscription> PushNotificationSubscriptions { get; set; }
    
    /// <summary>
    /// Table for notifications
    /// </summary>
    public DbSet<Notification> Notifications { get; set; }

    /// <summary>
    /// Table for Oauth apps
    /// </summary>
    
    public DbSet<OauthApp> OauthApps { get; set; }
    
    /// <summary>
    /// Table for Tags
    /// </summary>
    
    public DbSet<PlanetTag> Tags { get; set; }
    
    public DbSet<PermissionsNode> PermissionsNodes { get; set; }

    public DbSet<PlanetRole> PlanetRoles { get; set; }
    public DbSet<PlanetEmoji> PlanetEmojis { get; set; }
    public DbSet<PlanetRule> PlanetRules { get; set; }
    public DbSet<PlanetReport> PlanetReports { get; set; }

    public DbSet<PlanetThread> PlanetThreads { get; set; }
    public DbSet<ThreadComment> ThreadComments { get; set; }
    public DbSet<ThreadAttachment> ThreadAttachments { get; set; }
    public DbSet<ThreadBoost> ThreadBoosts { get; set; }
    public DbSet<ThreadCommentBoost> ThreadCommentBoosts { get; set; }

    public DbSet<UserChannelState> UserChannelStates { get; set; }
    
    public DbSet<NodeStats> NodeStats { get; set; }
    
    public DbSet<Report> Reports { get; set; }
    
    public DbSet<OldPlanetRoleMember> OldPlanetRoleMembers { get; set; }
    
    public DbSet<MessageReaction> MessageReactions { get; set; }
    public DbSet<MessageAttachment> MessageAttachments { get; set; }
    public DbSet<MessageMention> MessageMentions { get; set; }
    public DbSet<RealtimeKitMeeting> RealtimeKitMeetings { get; set; }
    
    

    ////////////////
    // Eco System //
    ////////////////

    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<Currency> Currencies { get; set; }
    public DbSet<EcoAccount> EcoAccounts { get; set; }
    
    ////////////////
    // CDN System //
    ////////////////
    
    public DbSet<CdnBucketItem> CdnBucketItems { get; set; }
    public DbSet<PlanetStorageConfig> PlanetStorageConfigs { get; set; }
    public DbSet<PlanetVoiceConfig> PlanetVoiceConfigs { get; set; }
    public DbSet<FederationKey> FederationKeys { get; set; }
    public DbSet<FederatedNode> FederatedNodes { get; set; }
    public DbSet<FederatedPlanetStub> FederatedPlanetStubs { get; set; }
    public DbSet<FederatedMigration> FederatedMigrations { get; set; }
    public DbSet<FederatedPurge> FederatedPurges { get; set; }
    public DbSet<FederatedAcceptedDomain> FederatedAcceptedDomains { get; set; }
    public DbSet<FederatedMembership> FederatedMemberships { get; set; }

    /// <summary>
    /// ASP.NET Data Protection key ring, persisted in the shared database so
    /// every node (and container restarts) can decrypt protected payloads
    /// such as planet storage credentials.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
    public DbSet<CdnProxyItem> CdnProxyItems { get; set; }
    
    ////////////
    // Themes //
    ////////////
    
    public DbSet<Theme> Themes { get; set; }
    public DbSet<ThemeVote> ThemeVotes { get; set; }
    public DbSet<ThemeAsset> ThemeAssets { get; set; }
    
    public ValourDb()
    {
        
    }
    
    public ValourDb(DbContextOptions<ValourDb> options) : base(options)
    {
        
    }
    
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.ConfigureWarnings(w => w.Ignore(RelationalEventId.ForeignKeyPropertiesMappedToUnrelatedTables));
        options.UseNpgsql(ConnectionString).UseExceptionProcessor();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Composite key
        modelBuilder.Entity<UserChannelState>().HasKey(x => new { x.UserId, x.ChannelId });

        // Soft delete
        modelBuilder.Entity<Planet>().HasQueryFilter(x => x.IsDeleted == false);
        modelBuilder.Entity<PlanetMember>().HasQueryFilter(x => x.IsDeleted == false);
        modelBuilder.Entity<Channel>().HasQueryFilter(x => x.IsDeleted == false);
        modelBuilder.Entity<PlanetThread>().HasQueryFilter(x => x.IsDeleted == false);

        // can only add query filters to root entities
        // modelBuilder.Entity<DirectChatChannel>().HasQueryFilter(x => x.IsDeleted == false);
        // modelBuilder.Entity<PlanetChannel>().HasQueryFilter(x => x.IsDeleted == false);
        // modelBuilder.Entity<PlanetChatChannel>().HasQueryFilter(x => x.IsDeleted == false);
        // modelBuilder.Entity<PlanetCategory>().HasQueryFilter(x => x.IsDeleted == false);
        // modelBuilder.Entity<PlanetVoiceChannel>().HasQueryFilter(x => x.IsDeleted == false); 
        
        //base.OnModelCreating(modelBuilder);
        
        MessageReaction.SetupDbModel(modelBuilder);
        MessageAttachment.SetupDbModel(modelBuilder);
        MessageMention.SetupDbModel(modelBuilder);
        RealtimeKitMeeting.SetupDbModel(modelBuilder);
        Message.SetupDbModel(modelBuilder);
        User.SetupDbModel(modelBuilder);
        UserSubscription.SetupDbModel(modelBuilder);
        UserChannelState.SetupDbModel(modelBuilder);
        PlanetMember.SetupDbModel(modelBuilder);
        PlanetRole.SetupDbModel(modelBuilder);
        PlanetEmoji.SetupDbModel(modelBuilder);
        PlanetRule.SetupDbModel(modelBuilder);
        PlanetReport.SetupDbModel(modelBuilder);
        PlanetThread.SetupDbModel(modelBuilder);
        ThreadComment.SetupDbModel(modelBuilder);
        ThreadAttachment.SetupDbModel(modelBuilder);
        ThreadBoost.SetupDbModel(modelBuilder);
        ThreadCommentBoost.SetupDbModel(modelBuilder);
        Report.SetupDbModel(modelBuilder);
        PlanetInvite.SetupDbModel(modelBuilder);
        AuthToken.SetupDbModel(modelBuilder);
        OauthApp.SetupDbModel(modelBuilder);
        Channel.SetupDbModel(modelBuilder);
        MultiAuth.SetupDbModel(modelBuilder);
        PushNotificationSubscription.SetUpDbModel(modelBuilder);
        Notification.SetupDbModel(modelBuilder);
        UserPrivateInfo.SetupDbModel(modelBuilder);
        Referral.SetupDbModel(modelBuilder);
        PlanetTag.SetupDbModel(modelBuilder);

        AutomodTrigger.SetupDbModel(modelBuilder);
        AutomodAction.SetupDbModel(modelBuilder);
        AutomodLog.SetupDbModel(modelBuilder);
        ModerationAuditLog.SetupDbModel(modelBuilder);

        Valour.Database.NodeStats.SetupDbModel(modelBuilder);
        OldPlanetRoleMember.SetupDbModel(modelBuilder);
        
        CdnBucketItem.SetupDbModel(modelBuilder);
        PlanetStorageConfig.SetupDbModel(modelBuilder);
        PlanetVoiceConfig.SetupDbModel(modelBuilder);
        Planet.SetupDbModel(modelBuilder);
        FederationKey.SetupDbModel(modelBuilder);
        FederatedNode.SetupDbModel(modelBuilder);
        FederatedPlanetStub.SetupDbModel(modelBuilder);
        FederatedMigration.SetupDbModel(modelBuilder);
        FederatedPurge.SetupDbModel(modelBuilder);
        FederatedAcceptedDomain.SetupDbModel(modelBuilder);
        FederatedMembership.SetupDbModel(modelBuilder);
        Transaction.SetupDbModel(modelBuilder);
    }
}

