using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Items;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Items.Planets;

namespace Valour.Database.Items.Planets;

public class Invite : Item, IPlanetItemAPI<Invite>, INodeSpecific
{
    [ForeignKey("Planet_Id")]
    [JsonIgnore]
    public virtual Planet Planet { get; set; }

    public override ItemType ItemType => ItemType.PlanetInvite;

    /// <summary>
    /// The invite code
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// The planet the invite is for
    /// </summary>
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The user that created the invite
    /// </summary>
    public ulong Issuer_Id { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    public DateTime Creation_Time { get; set; }

    /// <summary>
    /// The length of the invite before its invaild
    /// </summary>
    public int? Hours { get; set; }

    public bool IsPermanent() => Hours is null;

    public async Task<TaskResult> IsUserBanned(ulong user_Id, ValourDB db)
    {
        bool banned = await db.PlanetBans.AnyAsync(x => x.Target_Id == user_Id && x.Planet_Id == this.Planet_Id);
        if (banned)
            return new TaskResult(false, "User is banned from the planet");

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> CanGetAsync(PlanetMember member, ValourDB db) 
        => !await member.HasPermissionAsync(Shared.Authorization.PlanetPermissions.Invite, db)
            ? new TaskResult(false, "Member lacks PlanetPermissions.Invite")
            : TaskResult.SuccessResult;

    public async Task<TaskResult> CanDeleteAsync(PlanetMember member, ValourDB db)
        => await CanGetAsync(member, db);

    public Task<TaskResult> CanUpdateAsync(PlanetMember member, Invite old, ValourDB db)
    {
        if (this.Code != old.Code)
            return Task.FromResult(new TaskResult(false, "You cannot change the code"));
        if (this.Issuer_Id != old.Issuer_Id)
            return Task.FromResult(new TaskResult(false, "You cannot change who issued"));
        if (this.Creation_Time != old.Creation_Time)
            return Task.FromResult(new TaskResult(false, "You cannot change the creation time"));

        this.Issuer_Id = member.User_Id;
        return Task.FromResult(TaskResult.SuccessResult);
    }


    public async Task<TaskResult> CanCreateAsync(PlanetMember member, ValourDB db)
    {
        TaskResult canBan = await CanGetAsync(member, db);
        if (!canBan.Success)
            return canBan;

        this.Issuer_Id = member.User_Id;
        this.Creation_Time = DateTime.UtcNow;
        this.Code = await GenerateCode(db);

        return TaskResult.SuccessResult;
    }

    private static async Task<string> GenerateCode(ValourDB db)
    {
        Random random = new();

        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string code = "";

        bool exists = false;

        do
        {
            code = new string(Enumerable.Repeat(chars, 8).Select(s => s[random.Next(s.Length)]).ToArray());
            exists = await db.PlanetInvites.AnyAsync(x => x.Code == code);
        }
        while (exists);
        return code;
    }
}
