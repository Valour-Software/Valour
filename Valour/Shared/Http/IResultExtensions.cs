using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Valour.Shared.Authorization;

namespace Valour.Shared.Http
{
    public static class ValourResult
    {
        private struct NoTokenResult : IResult
        {
            public async Task ExecuteAsync(HttpContext httpContext)
            {
                httpContext.Response.StatusCode = 401;
                await httpContext.Response.WriteAsync("Missing token.");
            }
        }

        private struct NotPlanetMemberResult : IResult
        {
            public async Task ExecuteAsync(HttpContext httpContext)
            {
                httpContext.Response.StatusCode = 403;
                await httpContext.Response.WriteAsync("You are not a member of the target planet.");
            }
        }

        private struct LacksPermissionResult : IResult
        {
            private Permission _perm;

            public LacksPermissionResult(Permission perm)
            {
                _perm = perm;
            }

            public async Task ExecuteAsync(HttpContext httpContext)
            {
                httpContext.Response.StatusCode = 403;
                await httpContext.Response.WriteAsync($"User lacks " + _perm.PermissionType + " Permission " + _perm.Name);
            }
        }


        public static IResult NoToken() => new NoTokenResult();
        public static IResult NotPlanetMember() => new NotPlanetMemberResult();
        public static IResult LacksPermission(Permission permission) => new LacksPermissionResult(permission);
    }
}
