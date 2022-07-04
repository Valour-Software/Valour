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
            app.MapPost("/upload/{type}", UploadRoute);
        }

        private static async Task UploadRoute(HttpContext context, HttpClient http, ValourDB db,
            string type, [FromHeader] string authorization, long item_id = 0)
        {
            var authToken = await AuthToken.TryAuthorize(authorization, db);
            if (authToken == null) { await TokenInvalid(context); return; }

            if (string.IsNullOrWhiteSpace(type))
                Console.WriteLine("Include a valid upload type");

            if (context.Request.ContentLength < 512)
            {
                context.Response.StatusCode = 413;
                await context.Response.WriteAsync("Must be greater than 512 bytes");
                return;
            }

            // Max file size is 10mb
            if (context.Request.ContentLength > 10240000)
            {
                context.Response.StatusCode = 413;
                await context.Response.WriteAsync("Max file size is 10mb");
                return;
            }

            if (context.Request.Form.Files == null ||
                context.Request.Form.Files.Count == 0)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Include file");
                return;
            }

            var file = context.Request.Form.Files.FirstOrDefault();

            byte[] data = new byte[file.Length];

            await file.OpenReadStream().ReadAsync(data);

            var content = new MultipartFormDataContent();
            var arrContent = new ByteArrayContent(data);
            arrContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

            content.Add(arrContent, "file", file.Name);

            PlanetMember member = null;

            // Authorization

            if (type == "planet")
            {
                member = await db.PlanetMembers.Include(x => x.Planet)
                                               .FirstOrDefaultAsync(x => x.UserId == authToken.UserId &&
                                                                         x.PlanetId == item_id);

                if (member is null)
                {
                    await NotFound("Could not find member", context);
                    return;
                }

                if (!await member.HasPermissionAsync(PlanetPermissions.Manage, db))
                {
                    await Unauthorized("Member lacks PlanetPermissions.Manage", context);
                    return;
                }
            }

            OauthApp app = null;

            if (type == "app")
            {
                app = await db.OauthApps.FindAsync(item_id);

                if (app is null)
                {
                    await NotFound("Could not find app", context);
                    return;
                }

                if (app.OwnerId != authToken.UserId)
                {
                    await Unauthorized("You do not own the app!", context);
                    return;
                }
            }

            var response = await http.PostAsync($"https://vmps.valour.gg/Upload/{authToken.UserId}/{type}?auth={MPSConfig.Current.Api_Key_Encoded}", content);

            if (response.IsSuccessStatusCode)
            {
                switch (type)
                {
                    case "profile":
                        {
                            var user = await db.Users.FindAsync(authToken.UserId);
                            var url = await response.Content.ReadAsStringAsync();
                            user.PfpUrl = url;
                            await db.SaveChangesAsync();
                            PlanetHub.NotifyUserChange(user, db);
                            break;
                        }
                    case "planet":
                        {
                            var url = await response.Content.ReadAsStringAsync();
                            member.Planet.IconUrl = url;
                            await db.SaveChangesAsync();
                            PlanetHub.NotifyPlanetChange(member.Planet);
                            break;
                        }
                    case "app":
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


            context.Response.StatusCode = (int)response.StatusCode;
            await response.Content.CopyToAsync(context.Response.BodyWriter.AsStream());
        }
    }
}
