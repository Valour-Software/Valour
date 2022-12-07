using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Planets.Members;

namespace Valour.Server.Extensions
{
    public enum HttpVerbs
    {
        Get,
        Put,
        Post,
        Delete,
        Patch
    }

    public static class HttpClientExtensions
    {
        public static AuthToken GetToken(this HttpContext ctx)
        {
            //if (!ctx.Items.ContainsKey("token"))
            //    return null;

            return (AuthToken)ctx.Items["token"];
        }

        public static PlanetMember GetMember(this HttpContext ctx)
        {
            //if (!ctx.Items.ContainsKey("member"))
            //    return null;

            return (PlanetMember)ctx.Items["member"];
        }

        public static T GetItem<T>(this HttpContext ctx, object id)
        {
            //if (!ctx.Items.ContainsKey("member"))
            //    return null;

            return (T)ctx.Items[id];
        }

    }
}
