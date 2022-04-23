using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Items;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Items.Planets;
using Valour.Shared.Authorization;

namespace Valour.Database.Items.Planets;

public class Invite : PlanetItem<Invite>, INodeSpecific
{
    public override ItemType ItemType => ItemType.PlanetInvite;

    /// <summary>
    /// The invite code
    /// </summary>
    public string Code { get; set; }

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

    public override async Task<TaskResult> CanGetAsync(PlanetMember member, ValourDB db) 
        => !await member.HasPermissionAsync(PlanetPermissions.Invite, db)
            ? new TaskResult(false, "Member lacks Planet Permission" + PlanetPermissions.Invite.Name)
            : TaskResult.SuccessResult;

    public override async Task<TaskResult> CanDeleteAsync(PlanetMember member, ValourDB db)
        => await CanGetAsync(member, db);

    public override async Task<TaskResult> CanUpdateAsync(PlanetMember member, Invite old, ValourDB db)
    {
        TaskResult canGet = await CanGetAsync(member, db);
        if (!canGet.Success)
            return canGet;

        if (this.Code != old.Code)
            return await Task.FromResult(new TaskResult(false, "You cannot change the code"));
        if (this.Issuer_Id != old.Issuer_Id)
            return await Task.FromResult(new TaskResult(false, "You cannot change who issued"));
        if (this.Creation_Time != old.Creation_Time)
            return await Task.FromResult(new TaskResult(false, "You cannot change the creation time"));

        this.Issuer_Id = member.User_Id;
        return await Task.FromResult(TaskResult.SuccessResult);
    }


    public override async Task<TaskResult> CanCreateAsync(PlanetMember member, ValourDB db)
    {
        TaskResult canGet = await CanGetAsync(member, db);
        if (!canGet.Success)
            return canGet;

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
