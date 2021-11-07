using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Oauth;
using System.IO;

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
            app.MapPost("/upload/profileimage", ProfileImageRoute);
            app.MapPost("/upload/planetimage", PlanetImageRoute);
        }

        private static async Task ProfileImageRoute(HttpContext context, HttpClient http, ValourDB db)
        {
            if (!context.Request.Query.TryGetValue("auth", out var auth))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized. Include auth.");
                return;
            }

            var authToken = await ServerAuthToken.TryAuthorize(auth, db);

            if (authToken == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized.");
                return;
            }

            if (context.Request.ContentLength < 512)
            {
                context.Response.StatusCode = 413;
                await context.Response.WriteAsync("Must be greater than 512 bytes");
                return;
            }

            // Max file size is 2mb
            if (context.Request.ContentLength > 2621440)
            {
                context.Response.StatusCode = 413;
                await context.Response.WriteAsync("Max file size is 2mb");
                return;
            }

            int fileCount = context.Request.Form.Files.Count;

            Console.WriteLine("Count: " + fileCount);

            if (fileCount == 0)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Please attach an image");
                return;
            }

            if (fileCount > 1)
            {
                context.Response.StatusCode = 413;
                await context.Response.WriteAsync("Please attach one image only");
                return;
            }

            var file = context.Request.Form.Files[0];

            if (file.Length > 2621440)
            {
                context.Response.StatusCode = 413;
                await context.Response.WriteAsync("Max total size is 2mb");
                return;
            }

            // Ensure it conforms to limits

            if (!MPSManager.Image_Types.Contains(file.ContentType))
            {
                context.Response.StatusCode = 415;
                await context.Response.WriteAsync("Ensure file is an image.");
                return;
            }

            // Forward to VMPS

            byte[] data = new byte[file.Length];

            await file.OpenReadStream().ReadAsync(data);

            var content = new MultipartFormDataContent();
            var arrContent = new ByteArrayContent(data);
            arrContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

            content.Add(arrContent, "file", file.Name);

            var response = await http.PostAsync($"https://vmps.valour.gg/Upload/ProfileImage?auth={MPSConfig.Current.Api_Key_Encoded}", content);

            context.Response.StatusCode = (int)response.StatusCode;
            await response.Content.CopyToAsync(context.Response.BodyWriter.AsStream());

            // Change user pfp in database
            if (response.IsSuccessStatusCode)
            {
                var user = await db.Users.FindAsync(authToken.User_Id);
                user.Pfp_Url = await response.Content.ReadAsStringAsync();
                await db.SaveChangesAsync();
            }
        }

        private static async Task PlanetImageRoute(HttpContext context, HttpClient http, ValourDB db, ulong planet_id)
        {
            if (!context.Request.Query.TryGetValue("auth", out var auth))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized. Include auth.");
                return;
            }

            var authToken = await ServerAuthToken.TryAuthorize(auth, db);

            if (authToken == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized.");
                return;
            }

            if (context.Request.ContentLength < 512)
            {
                context.Response.StatusCode = 413;
                await context.Response.WriteAsync("Must be greater than 512 bytes");
                return;
            }

            // Max file size is 2mb
            if (context.Request.ContentLength > 8388608)
            {
                context.Response.StatusCode = 413;
                await context.Response.WriteAsync("Max file size is 8mb");
                return;
            }

            int fileCount = context.Request.Form.Files.Count;

            Console.WriteLine("Count: " + fileCount);

            if (fileCount == 0)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Please attach an image");
                return;
            }

            if (fileCount > 1)
            {
                context.Response.StatusCode = 413;
                await context.Response.WriteAsync("Please attach one image only");
                return;
            }

            var file = context.Request.Form.Files[0];

            if (file.Length > 8388608)
            {
                context.Response.StatusCode = 413;
                await context.Response.WriteAsync("Max total size is 8mb");
                return;
            }

            // Ensure it conforms to limits

            if (!MPSManager.Image_Types.Contains(file.ContentType))
            {
                context.Response.StatusCode = 415;
                await context.Response.WriteAsync("Ensure file is an image.");
                return;
            }

            // Forward to VMPS

            byte[] data = new byte[file.Length];

            await file.OpenReadStream().ReadAsync(data);

            var content = new MultipartFormDataContent();
            var arrContent = new ByteArrayContent(data);
            arrContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

            content.Add(arrContent, "file", file.Name);

            var response = await http.PostAsync($"https://vmps.valour.gg/Upload/PlanetImage?auth={MPSConfig.Current.Api_Key_Encoded}", content);

            context.Response.StatusCode = (int)response.StatusCode;
            await response.Content.CopyToAsync(context.Response.BodyWriter.AsStream());

            // Change user pfp in database
            if (response.IsSuccessStatusCode)
            {
                var planet = await db.Planets.FindAsync(planet_id);
                planet.Image_Url = await response.Content.ReadAsStringAsync();
                await db.SaveChangesAsync();
            }
        }
    }
}
