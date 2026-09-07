# API routing and authorization

Valour uses ASP.NET Core minimal APIs. Route handlers are public static methods
marked with `ValourRoute`. Startup discovers them in the server assembly and
attaches the access checks and rate limits declared on each method.

## Route registration

`DynamicAPI.RegisterAll(app)` in `Valour/Server/Program.cs` scans the assembly once.
For each attributed method it creates a delegate and registers GET, POST, PUT,
PATCH, or DELETE routes. A method can declare multiple routes. Non-static handlers,
unsupported verbs, and duplicate verb/path pairs cause startup to fail. Duplicate
path comparison ignores case.

Handlers usually live in `Valour/Server/Api/Dynamic/`. Adding an attributed method
in that assembly is enough for discovery. Register any services it requires in
the dependency injection container. ASP.NET binds route parameters, request bodies,
and injected services to the handler's parameters.

For a complete route example, see `PlanetRoleApi.GetRouteAsync` in
`Valour/Server/Api/Dynamic/PlanetRoleApi.cs`. It loads a role using both the planet
and role IDs, checks the caller's membership, and returns JSON or an error result.

## Authentication and token scope

Clients send their session token in the `Authorization` header. `TokenService`
resolves it from its token cache or the database and rejects expired tokens.
`GetCurrentTokenAsync` exposes the current request's token to services.

`UserRequired` declares the token scopes an endpoint needs. Scopes limit what a
client or OAuth application may do on behalf of the account. They do not establish
planet membership or grant permission to change a resource.

`DynamicAPI` combines `UserRequired` and `StaffRequired` into a `UserAccessFilter`.
The filter resolves services from the current request, requires a valid token,
rejects missing or disabled accounts, checks the staff flag when requested, and
checks every declared scope. The filter is shared across requests and holds no
request-specific state.

Use `UserRequired` for an authenticated endpoint and `StaffRequired` for staff
operations. The complete scope definitions are in
`Valour/Shared/Authorization/Permissions.cs`; use those definitions when
choosing an endpoint's requirements.

## Resource authorization

After authentication, the handler or service must check access to the actual
resource. A planet operation generally requires membership and the relevant planet
permission. Channel operations also check the channel's permissions. An action
against another member or role may require greater authority than its target.
See [Roles and permissions](ROLES.md) for the hierarchy and role lifecycle.

Validate that IDs in the request body agree with route IDs. Load child resources
within their planet's scope. For example, having permission in one planet does
not authorize changing a role from another planet. Resource ownership, feature
settings, and a planet's migration lock can impose additional restrictions.

Staff status and planet authority are separate checks. Template publication,
for example, requires staff status as well as membership and Manage Village in
the source planet. Follow the requirements of the service being called rather
than assuming that a staff route can skip its normal authorization.

## Node routing

Every discovered route receives `NotHostedExceptionFilter`. When a planet service
throws `PlanetNotHostedException`, the filter returns a not-found response for a
missing planet or `ValourResult.WrongNode` for a planet assigned elsewhere. The
SDK uses the resulting routing information to retry against the appropriate node.

Community nodes are separate origins. Their local sessions and planet-scoped
resources follow the federation checks described in
[the federation architecture](../../Docs/FederationArchitecture.md).

## Responses

Return models with `Results.Json`, newly created resources with `Results.Created`,
and empty successful deletions with `Results.NoContent` where appropriate.
`ValourResult` supplies the shared error responses:

| Result | Meaning |
| --- | --- |
| `InvalidToken()` | Authentication failed |
| `BadRequest(message)` | The request is invalid |
| `Forbid(message)` | The caller cannot perform the action |
| `NotPlanetMember()` | Required planet membership is missing |
| `LacksPermission(permission)` | A required permission is missing |
| `NotFound(message)` | The requested resource does not exist |
| `Problem(message)` | The operation failed on the server |
| `WrongNode(node, planetId)` | The request belongs on another node |

Services commonly return `TaskResult` or `TaskResult<T>`. Check `Success` before
reading `Data`, then translate the result into the endpoint's HTTP response.
Avoid returning internal exception details to clients; keep diagnostic context
in server logs.

## Rate limits and verification

`RateLimit` names a registered rate-limit policy. Route discovery applies it
with `RequireRateLimiting`. Inspect the policy registration in the server before
choosing a name or documenting a limit.

For a route change, verify successful access, invalid input, missing authentication,
insufficient scope, and unauthorized access to another user's or planet's data.
For planet routes, include the correct-node behavior where relevant. Run
integration tests against isolated data services as described in
[the test guide](../Tests/Browser/README.md#isolated-c-regression).
