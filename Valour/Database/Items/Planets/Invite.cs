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
    /// the invite code
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
        bool banned = await db.PlanetBans.AnyAsync(x => x.User_Id == user_Id && x.Planet_Id == this.Planet_Id);
        if (banned)
            return new TaskResult(false, "User is banned from the planet");

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Success if a member has invite permission
    /// and that the planet is private to get this 
    /// invite via the API.
    /// </summary>
    public async Task<TaskResult> CanGetAsync(PlanetMember member, ValourDB db)
    {
        if (member is null)
            return new TaskResult(false, "Member not found.");

        if (!await member.HasPermissionAsync(Shared.Authorization.PlanetPermissions.Invite, db))
            return new TaskResult(false, "Member lacks PlanetPermissions.Invite");

        return TaskResult.SuccessResult;
    }

    public Task<TaskResult> CanDeleteAsync(PlanetMember member, ValourDB db)
        => Task.FromResult(TaskResult.SuccessResult);

    //Setting the Issuer_Id here is stupid it would be a good idea to fix this somehow
    public Task<TaskResult> CanUpdateAsync(PlanetMember member, ValourDB db)
    {
        this.Issuer_Id = member.User_Id;
        return Task.FromResult(TaskResult.SuccessResult);
    }


    public async Task<TaskResult> CanCreateAsync(PlanetMember member, ValourDB db)
    {
        this.Issuer_Id = member.User_Id;
        return await CanGetAsync(member, db);
    }

    public async Task<TaskResult> ValidateItemAsync(Invite old, ValourDB db)
    {
        Planet planet = await ((IPlanetItemAPI<PlanetRole>)this).GetPlanetAsync(db);

        // This role is new
        if (old is null)
        {
            // TODO Figure out how to fill in Issure_ID
            // The stupid solution to this is above
            //this.Issuer_Id = member.User_Id;
            this.Creation_Time = DateTime.UtcNow;

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

            this.Code = code;
        }
        else
        {
            if (this.Code != old.Code)
                return new TaskResult(false, "You cannot change the code");
            if (this.Issuer_Id != old.Issuer_Id)
                return new TaskResult(false, "You cannot change who issued");
            if (this.Creation_Time != old.Creation_Time)
                return new TaskResult(false, "You cannot change the creation time");
        }

        if (this.Hours < 1)
            this.Hours = null;

        return TaskResult.SuccessResult;
    }
}
