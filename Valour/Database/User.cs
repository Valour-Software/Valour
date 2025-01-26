using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database
{
    public class User : ISharedUser
    {
        ///////////////////////////
        // Relational Properties //
        ///////////////////////////
        
        /// <summary>
        /// Private info associated with this user.
        /// </summary>
        public virtual UserPrivateInfo PrivateInfo { get; set; }

        /// <summary>
        /// The membership of this user in different planets.
        /// </summary>
        public virtual ICollection<PlanetMember> Membership { get; set; }
        
        /// <summary>
        /// The membership of this user in different planets.
        /// </summary>
        public virtual ICollection<PlanetRoleMember> Memberships { get; set; }
        
        /// <summary>
        /// The membership of this user in different channels.
        /// </summary>
        public virtual ICollection<ChannelMember> ChannelMembership { get; set; }
        
        /// <summary>
        /// User ownedApps
        /// </summary>
        public virtual ICollection<OauthApp> OwnedApps { get; set; }
        
        /// <summary>
        /// User OwnedTokens
        /// </summary>
        public virtual ICollection<AuthToken> OwnedTokens { get; set; }
        
        /// <summary>
        /// All messages sent by this user.
        /// </summary>
        public virtual ICollection<Message> Messages { get; set; }
        
        /// <summary>
        /// Subscriptions this user has or had
        /// </summary>
        public virtual ICollection<UserSubscription> Subscriptions { get; set; }
        
        /// <summary>
        /// Channel states for this user.
        /// </summary>
        public virtual ICollection<UserChannelState> ChannelStates { get; set; }
        
        /// <summary>
        /// Privateinfo for this user.
        /// </summary>
        public virtual ICollection<UserPrivateInfo> UserPrivateInfo { get; set; }
        
        /// <summary>
        /// Rewards for this user.
        /// </summary>
        public virtual ICollection<Referral> Rewards { get; set; }
        
        /// <summary>
        /// Notification of Subscriptions for this user.
        /// </summary>
        public virtual ICollection<NotificationSubscription> NotificationSubscriptions { get; set; }
        
        
        ///////////////////////
        // Entity Properties //
        ///////////////////////
        
        /// <summary>
        /// The unique ID of the user.
        /// </summary>
        public long Id { get; set; }
        
        /// <summary>
        /// True if the user has a custom profile picture.
        /// </summary>
        public bool HasCustomAvatar { get; set; }
        
        /// <summary>
        /// True if the user has an animated profile picture.
        /// </summary>
        public bool HasAnimatedAvatar { get; set; }
        
        /// <summary>
        /// Old avatar url. Do not use.
        /// </summary>
        public string OldAvatarUrl { get; set; }

        /// <summary>
        /// The Date and Time that the user joined Valour.
        /// </summary>
        public DateTime TimeJoined { get; set; }

        /// <summary>
        /// The name of this user.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// True if the user is a bot.
        /// </summary>
        public bool Bot { get; set; }

        /// <summary>
        /// True if the account has been disabled.
        /// </summary>
        public bool Disabled { get; set; }

        /// <summary>
        /// True if this user is a member of the Valour official staff team. 
        /// Falsely modifying this through a client modification to present non-official staff as staff is a breach of our license. Don't do that.
        /// </summary>
        public bool ValourStaff { get; set; }

        /// <summary>
        /// The user's currently set status - this could represent how they feel, 
        /// their disdain for the political climate of the modern world, their love for their mother's cooking,
        /// or their hate for lazy programmers.
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// The integer representation of the current user state.
        /// </summary>
        public int UserStateCode { get; set; }

        /// <summary>
        /// The last time this user was flagged as active (successful auth).
        /// </summary>
        public DateTime TimeLastActive { get; set; }
        
        /// <summary>
        /// True if the user has been recently on a mobile device.
        /// </summary>
        public bool IsMobile { get; set; }
        
        /// <summary>
        /// The tag (discriminator) of this user.
        /// </summary>
        public string Tag { get; set; }
        
        /// <summary>
        /// If the user has completed the compliance step for regulatory purposes.
        /// This should only ever be false on legacy or testing accounts.
        /// </summary>
        public bool Compliance { get; set; }
        
        /// <summary>
        /// If not null, the type of UserSubscription the user currently is subscribed to.
        /// </summary>
        public string SubscriptionType { get; set; }

        /// <summary>
        /// The user's prior username, if they have changed it before.
        /// </summary>
        public string PriorName { get; set; }

        /// <summary>
        /// The date and time the user last changed their username.
        /// </summary>
        public DateTime? NameChangeTime { get; set; }

        /// <summary>
        /// Generates the avatar URL for this user based on the requested format.
        /// </summary>
        public string GetAvatarUrl(AvatarFormat format = AvatarFormat.Webp256) =>
            ISharedUser.GetAvatar(this, format);

        /// <summary>
        /// Configures the entity model for the `User` class using fluent configuration.
        /// </summary>
        public static void SetupDbModel(ModelBuilder builder)
        {
            builder.Entity<User>(e =>
            {
                // Table
                e.ToTable("users");

                // Keys
                e.HasKey(x => x.Id);

                // Properties
                e.Property(x => x.Id)
                    .HasColumnName("id");
                
                e.Property(x => x.HasCustomAvatar)
                    .HasColumnName("custom_avatar");
                
                e.Property(x => x.HasAnimatedAvatar)
                    .HasColumnName("animated_avatar");
                
                e.Property(x => x.OldAvatarUrl)
                    .HasColumnName("pfp_url");
                
                e.Property(x => x.TimeJoined)
                    .HasColumnName("time_joined")
                    .HasConversion(
                        x => x,
                        x => new DateTime(x.Ticks, DateTimeKind.Utc)
                    );
                
                e.Property(x => x.Name)
                    .HasColumnName("name");
                
                e.Property(x => x.Bot)
                    .HasColumnName("bot");
                
                e.Property(x => x.Disabled)
                    .HasColumnName("disabled");
                
                e.Property(x => x.ValourStaff)
                    .HasColumnName("valour_staff");
                
                e.Property(x => x.Status)
                    .HasColumnName("status");
                
                e.Property(x => x.UserStateCode)
                    .HasColumnName("user_state_code");
                
                e.Property(x => x.TimeLastActive)
                    .HasColumnName("time_last_active")
                    .HasConversion(
                        x => x,
                        x => new DateTime(x.Ticks, DateTimeKind.Utc)
                    );
                
                e.Property(x => x.IsMobile)
                    .HasColumnName("is_mobile");
                
                e.Property(x => x.Tag)
                    .HasColumnName("tag");
                
                e.Property(x => x.Compliance)
                    .HasColumnName("compliance");
                
                e.Property(x => x.SubscriptionType)
                    .HasColumnName("subscription_type");
                
                e.Property(x => x.PriorName)
                    .HasColumnName("prior_name");
                
                e.Property(x => x.NameChangeTime)
                    .HasColumnName("name_change_time")
                    .HasConversion(
                        x => x,
                        x => x == null ? null : new DateTime(x.Value.Ticks, DateTimeKind.Utc)
                    );

                // Relationships
                e.HasOne(x => x.PrivateInfo)
                    .WithOne(x => x.User)
                    .HasForeignKey<UserPrivateInfo>(x => x.UserId);

                e.HasMany(x => x.Membership)
                    .WithOne(x => x.User)
                    .HasForeignKey(x => x.UserId);

                e.HasMany(x => x.Messages)
                    .WithOne(x => x.AuthorUser)
                    .HasForeignKey(x => x.AuthorUserId);

                // Indices
                e.HasIndex(x => new { x.Tag, x.Name })
                    .IsUnique();

                e.HasIndex(x => x.TimeLastActive);
            });
        }
    }
}
