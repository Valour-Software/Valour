using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Shared.Roles;
using Valour.Shared.Oauth;
using System.IO;

using Microsoft.EntityFrameworkCore;

using Microsoft.AspNetCore.Hosting;
using Valour.Server.MPS;
using Microsoft.AspNetCore.Builder;
using System.Net.Http.Headers;

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
            string type, [FromHeader] string authorization, ulong planet_id = 0)
        {
            var authToken = await ServerAuthToken.TryAuthorize(authorization, db);
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

            ServerPlanetMember member = null;

            // Authorization

            if (type == "planet"){
                member = await db.PlanetMembers.Include(x => x.Planet)
                                               .FirstOrDefaultAsync(x => x.User_Id == authToken.User_Id &&
                                                                         x.Planet_Id == planet_id);
                
                if (!await member.HasPermissionAsync(PlanetPermissions.Manage, db)){
                    await Unauthorized("Member lacks PlanetPermissions.Manage", context);
                    return;
                }
            }

            var response = await http.PostAsync($"https://vmps.valour.gg/Upload/{authToken.User_Id}/{type}?auth={MPSConfig.Current.Api_Key_Encoded}", content);

            if (response.IsSuccessStatusCode){
                switch (type){
                    case "profile": 
                    {
                        var user = await db.Users.FindAsync(authToken.User_Id);
                        var url = await response.Content.ReadAsStringAsync();
                        user.Pfp_Url = url;
                        await db.SaveChangesAsync();
                        break;
                    }
                    case "planet": 
                    {
                        var url = await response.Content.ReadAsStringAsync();
                        member.Planet.Image_Url = url;
                        await db.SaveChangesAsync();
                        PlanetHub.NotifyPlanetChange(member.Planet);
                        break;
                    }
                }
            }


            context.Response.StatusCode = (int)response.StatusCode;
            await response.Content.CopyToAsync(context.Response.BodyWriter.AsStream());
        }
    }
}
