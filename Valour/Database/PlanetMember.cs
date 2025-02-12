﻿using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

/// <summary>
/// Database model for a planet member
/// </summary>
public class PlanetMember : ISharedPlanetMember
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [JsonIgnore]
    public Planet Planet { get; set; }
    
    [JsonIgnore]
    public virtual User User { get; set; }
    
    [JsonIgnore]
    public virtual ICollection<PlanetRoleMember> RoleMembership { get; set; }
    
    [JsonIgnore]
    public virtual ICollection<Message> Messages { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long UserId { get; set; }
    public long PlanetId { get; set; }
    public string Nickname { get; set; }
    public string MemberAvatar { get; set; }
    public bool IsDeleted { get; set; }
    public long RoleMembershipHash { get; set; }

    /// <summary>
    /// Configures the entity model for the `PlanetMember` class using fluent configuration.
    /// </summary>
    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetMember>(e =>
        {
            // Table
            e.ToTable("planet_members");
            
            // Keys
            e.HasKey(x => x.Id);
            
            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.UserId)
                .HasColumnName("user_id");
            
            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.Nickname)
                .HasColumnName("nickname")
                .HasMaxLength(32);
            
            e.Property(x => x.MemberAvatar)
                .HasColumnName("member_pfp");
            
            e.Property(x => x.IsDeleted)
                .HasColumnName("is_deleted");
            
            e.Property(x => x.RoleMembershipHash)
                .HasColumnName("role_hash_key");
            
            // Relationships

            e.HasOne(x => x.Planet)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.PlanetId);

            e.HasOne(x => x.User)
                .WithMany(x => x.Membership)
                .HasForeignKey(x => x.UserId);
            
            e.HasMany(x => x.RoleMembership)
                .WithOne(x => x.Member)
                .HasForeignKey(x => x.MemberId);

            e.HasMany(x => x.Messages)
                .WithOne(x => x.AuthorMember)
                .HasForeignKey(x => x.AuthorMemberId);
            
            // Indices
            e.HasIndex(x => new { x.UserId, x.PlanetId })
                .IsUnique();

            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.PlanetId);
            e.HasIndex(x => x.RoleMembershipHash);
        });
    }
}

