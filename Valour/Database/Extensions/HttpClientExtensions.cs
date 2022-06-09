using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets.Members;

namespace Valour.Database.Extensions
{
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

        public static ValourDB GetDB(this HttpContext ctx)
        {
            return (ValourDB)ctx.Items["db"];
        }

    }
}
