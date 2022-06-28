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

        private struct ForbidResult : IResult
        {
            private string _reason;

            public ForbidResult(string reason)
            {
                _reason = reason;
            }

            public async Task ExecuteAsync(HttpContext httpContext)
            {
                httpContext.Response.StatusCode = 403;
                await httpContext.Response.WriteAsync(_reason);
            }
        }

        private struct InvalidTokenResult : IResult
        {
            public async Task ExecuteAsync(HttpContext httpContext)
            {
                httpContext.Response.StatusCode = 401;
                await httpContext.Response.WriteAsync("Token is invalid.");
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

        private struct NotFoundResult<T> : IResult
        {
            public async Task ExecuteAsync(HttpContext httpContext)
            {
                httpContext.Response.StatusCode = 404;
                await httpContext.Response.WriteAsync($"Object of type {typeof(T).Name} was not found.");
            }
        }

        public static IResult NotFound<T>() => new NotFoundResult<T>();
        public static IResult Forbid(string reason) => new ForbidResult(reason);
        public static IResult NoToken() => new NoTokenResult();
        public static IResult InvalidToken() => new InvalidTokenResult();
        public static IResult NotPlanetMember() => new NotPlanetMemberResult();
        public static IResult LacksPermission(Permission permission) => new LacksPermissionResult(permission);
    }
}
