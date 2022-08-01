using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.MPS;

namespace Valour.Server.API
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */


    /// <summary>
    /// Responsible for allowing user uploads of content
    /// </summary>
    public class UploadAPI : BaseAPI
    {
        public static void AddRoutes(WebApplication app)
        {
            app.MapPost("/upload/{category}", UploadRoute);
        }

        private static async Task<IResult> UploadRoute(HttpContext context, HttpClient http, ValourDB db,
            string category, [FromHeader] string authorization, long itemId = 0)
        {
            var authToken = await AuthToken.TryAuthorize(authorization, db);
            if (authToken == null) return ValourResult.NoToken();

            if (string.IsNullOrWhiteSpace(category))
                return Results.BadRequest("Include content category.");

            if (context.Request.ContentLength < 512)
                return Results.BadRequest("Must be greater than 512 bytes");

            // Max file size is 10mb
            if (context.Request.ContentLength > 10240000)
                return Results.BadRequest("Max file size is 10mb");

            if (context.Request.Form.Files == null ||
                context.Request.Form.Files.Count == 0)
                return Results.BadRequest("Include file");

            var file = context.Request.Form.Files.FirstOrDefault();

            byte[] data = new byte[file.Length];

            var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(file.OpenReadStream());

            streamContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

            content.Add(streamContent, "file", file.Name);

            PlanetMember member = null;

            // Authorization

            if (category == "Planet")
            {
                member = await db.PlanetMembers.Include(x => x.Planet)
                                               .FirstOrDefaultAsync(x => x.UserId == authToken.UserId &&
                                                                         x.PlanetId == itemId);

                if (member is null)
                    return Results.NotFound("Could not find member");


                if (!await member.HasPermissionAsync(PlanetPermissions.Manage, db))
                    return Results.Unauthorized();
            }

            OauthApp app = null;

            if (category == "App")
            {
                app = await db.OauthApps.FindAsync(itemId);

                if (app is null)
                    return Results.NotFound("Could not find app.");

                if (app.OwnerId != authToken.UserId)
                    return Results.Unauthorized();
            }

            var response = await http.PostAsync($"https://vmps.valour.gg/Upload/{authToken.UserId}/{category}?auth={MPSConfig.Current.Api_Key_Encoded}", content);

            if (response.IsSuccessStatusCode)
            {
                switch (category)
                {
                    case "Profile":
                        {
                            var user = await db.Users.FindAsync(authToken.UserId);
                            var url = await response.Content.ReadAsStringAsync();
                            user.PfpUrl = url;
                            await db.SaveChangesAsync();
                            PlanetHub.NotifyUserChange(user, db);
                            break;
                        }
                    case "Planet":
                        {
                            var url = await response.Content.ReadAsStringAsync();
                            member.Planet.IconUrl = url;
                            await db.SaveChangesAsync();
                            PlanetHub.NotifyPlanetChange(member.Planet);
                            break;
                        }
                    case "App":
                        {
                            var url = await response.Content.ReadAsStringAsync();
                            app.ImageUrl = url;
                            await db.SaveChangesAsync();
                            break;
                        }
                    default:
                        break;

                }
            }

            return ValourResult.Ok(await response.Content.ReadAsStringAsync());
        }
    }
}
