# Valour API Routing System

This document outlines how API routes are defined, registered, and secured in the Valour server.

## Table of Contents

1. [Route Registration](#route-registration)
2. [Authentication](#authentication)
3. [Authorization Attributes](#authorization-attributes)
4. [Permission System](#permission-system)
5. [Staff Routes](#staff-routes)
6. [Creating New Routes](#creating-new-routes)
7. [Response Patterns](#response-patterns)

---

## Route Registration

Valour uses **ASP.NET Core Minimal APIs** with a custom `DynamicAPI<T>` pattern for route registration.

### Defining Routes

Routes are defined as static methods decorated with the `[ValourRoute]` attribute:

```csharp
[ValourRoute(HttpVerbs.Get, "api/users/{id}")]
public static async Task<IResult> GetUserRouteAsync(
    long id,
    UserService userService)
{
    var user = await userService.GetAsync(id);
    return user is null ? ValourResult.NotFound<User>() : Results.Json(user);
}
```

### HttpVerbs

Available HTTP methods (defined in `HttpClientExtensions.cs`):
- `HttpVerbs.Get`
- `HttpVerbs.Post`
- `HttpVerbs.Put`
- `HttpVerbs.Delete`
- `HttpVerbs.Patch`

### Registration in Program.cs

Routes are registered in `Program.cs`:

```csharp
DynamicApis = new()
{
    new DynamicAPI<UserApi>().RegisterRoutes(app),
    new DynamicAPI<PlanetApi>().RegisterRoutes(app),
    new DynamicAPI<ChannelApi>().RegisterRoutes(app),
    new DynamicAPI<StaffApi>().RegisterRoutes(app),
    // ... more APIs
};
```

The `DynamicAPI<T>.RegisterRoutes()` method uses reflection to:
1. Scan all static methods for `[ValourRoute]` attributes
2. Create delegates for each method
3. Map routes using `app.MapGet()`, `app.MapPost()`, etc.
4. Apply endpoint filters for authentication/authorization

---

## Authentication

Valour uses **token-based authentication** via the `Authorization` header.

### Token Structure

```csharp
public class AuthToken
{
    public string Id { get; set; }           // Token ID (the actual token string)
    public string AppId { get; set; }        // OAuth app ID (null for first-party)
    public long UserId { get; set; }         // User ID
    public long Scope { get; set; }          // Permission bitmask
    public DateTime TimeCreated { get; set; }
    public DateTime TimeExpires { get; set; }
    public string IssuedAddress { get; set; }
}
```

### Token Validation Flow

1. **Extraction**: Token read from `Authorization` header
2. **Cache Check**: Checked in memory cache first (ConcurrentDictionary)
3. **Database Lookup**: Falls back to database if not cached
4. **Expiration Check**: Removes and rejects expired tokens

### TokenService Usage

```csharp
// Get current token
var token = await tokenService.GetCurrentTokenAsync();

// Get current user
var user = await userService.GetCurrentUserAsync();
```

---

## Authorization Attributes

### Available Attributes

| Attribute | Purpose | Location |
|-----------|---------|----------|
| `[ValourRoute(HttpVerbs, string)]` | Define route path and method | `RouteAttribute.cs` |
| `[UserRequired(params UserPermissionsEnum[])]` | Require valid token with specific scopes | `UserPermissionsFilter.cs` |
| `[StaffRequired]` | Require Valour staff flag | `StaffRequiredFilter.cs` |
| `[RateLimit(string)]` | Apply rate limiting policy | `RateLimitDefs.cs` |

### UserRequired Scopes

```csharp
public enum UserPermissionsEnum
{
    FullControl,        // All permissions
    Minimum,            // Basic authentication
    View,               // View public data
    Membership,         // Planet membership operations
    Invites,            // Invite operations
    PlanetManagement,   // Planet admin operations
    Messages,           // Read/send messages
    Friends,            // Friend operations
    DirectMessages,     // DM operations
    EconomyPlanetView,  // View planet economy
    EconomyPlanetSend,  // Send planet currency
    EconomyViewGlobal,  // View global economy
    EconomySendGlobal,  // Send global currency
    EconomyPlanetTrade, // Planet trading
    EconomyGlobalTrade  // Global trading
}
```

### Example: Stacking Attributes

```csharp
[ValourRoute(HttpVerbs.Post, "api/staff/disable")]
[UserRequired(UserPermissionsEnum.FullControl)]
[StaffRequired]
[RateLimit("default")]
public static async Task<IResult> DisableUserAsync(...)
```

---

## Permission System

Valour has a **three-tier permission system**:

### 1. User/Token Scope Permissions

Controls what an OAuth application can do on behalf of a user. Checked via `[UserRequired]`.

### 2. Planet Permissions (Role-Based)

Controls what a member can do within a planet. Defined in `PlanetPermissions.cs`:

```csharp
public enum PlanetPermissionsEnum
{
    FullControl,
    View,
    Invite,
    DisplayRole,
    Manage,
    Kick,
    Ban,
    ManageCategories,
    ManageChannels,
    ManageRoles,
    UseEconomy,
    ManageCurrency,
    ManageEcoAccounts,
    ForceTransactions,
    BypassAutomod,
}
```

**Checking Planet Permissions:**

```csharp
var member = await memberService.GetCurrentAsync(planetId);
if (member is null)
    return ValourResult.NotPlanetMember();

if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Ban))
    return ValourResult.LacksPermission(PlanetPermissions.Ban);
```

### 3. Channel Permissions

Granular permissions for specific channels. Three types:

- **Chat Channel**: View, ViewMessages, PostMessages, ManageChannel, ManagePermissions, Embed, AttachContent, ManageMessages, UseEconomy, UseReactions
- **Category**: View, ManageCategory, ManagePermissions
- **Voice Channel**: View, Join, Speak, ManageChannel, ManagePermissions

**Checking Channel Permissions:**

```csharp
if (!await channelService.HasPermissionAsync(channel, member, ChatChannelPermissions.ManageChannel))
    return ValourResult.Forbid("You do not have permission to manage this channel");
```

### Authority Hierarchy

When performing actions on other members, authority must be checked:

```csharp
var targetAuthority = await memberService.GetAuthorityAsync(target);
var memberAuthority = await memberService.GetAuthorityAsync(member);

if (targetAuthority >= memberAuthority)
    return ValourResult.Forbid("The target has equal or higher authority than you.");
```

---

## Staff Routes

### StaffRequired Filter

The `[StaffRequired]` attribute checks the `ValourStaff` flag on the user:

```csharp
public class StaffRequiredFilter : IEndpointFilter
{
    public async ValueTask<object> InvokeAsync(...)
    {
        var user = await _userService.GetCurrentUserAsync();
        if (user is null)
            return ValourResult.Forbid("User not found");

        if (!user.ValourStaff)
            return ValourResult.Forbid("This endpoint is staff only");

        return await next(ctx);
    }
}
```

### Current Staff Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `api/staff/reports` | GET | Get all reports |
| `api/staff/reports/query` | POST | Query reports with filters |
| `api/staff/reports/{id}/reviewed/{value}` | PUT | Mark report reviewed |
| `api/staff/disable` | POST | Disable user account |
| `api/staff/delete` | POST | Delete user (SuperAdmin only) |
| `api/staff/messages/{id}` | GET | Get specific message |
| `api/staff/users/query` | POST | Query users |

### SuperAdmin Check

Some operations require SuperAdmin (currently hardcoded):

```csharp
if (requestor.Id != 12200448886571008) // Spike's user ID
{
    return ValourResult.Forbid("SuperAdmins only.");
}
```

---

## Creating New Routes

### Step 1: Choose or Create an API Class

Routes are organized into API classes in `/Server/Api/Dynamic/`:

- `UserApi.cs` - User operations
- `PlanetApi.cs` - Planet operations
- `ChannelApi.cs` - Channel operations
- `StaffApi.cs` - Staff operations
- etc.

### Step 2: Define the Route Method

```csharp
[ValourRoute(HttpVerbs.Post, "api/example/{id}")]
[UserRequired(UserPermissionsEnum.FullControl)]
public static async Task<IResult> ExampleRouteAsync(
    long id,                              // Route parameter
    [FromBody] ExampleRequest request,    // Request body
    ExampleService exampleService,        // Injected service
    UserService userService)              // Injected service
{
    // 1. Get current user
    var user = await userService.GetCurrentUserAsync();

    // 2. Validate permissions (if needed beyond attributes)

    // 3. Perform operation
    var result = await exampleService.DoSomething(id, request);

    // 4. Return result
    if (!result.Success)
        return ValourResult.BadRequest(result.Message);

    return Results.Json(result.Data);
}
```

### Step 3: Register the API (if new class)

Add to `Program.cs`:

```csharp
DynamicApis = new()
{
    // ... existing APIs
    new DynamicAPI<ExampleApi>().RegisterRoutes(app),
};
```

### Request/Response Models

Define request models in `/Shared/Requests/` or `/Server/Requests/`:

```csharp
public class ExampleRequest
{
    public string Name { get; set; }
    public long Value { get; set; }
}
```

---

## Response Patterns

Use `ValourResult` for consistent responses:

| Method | Status | Use Case |
|--------|--------|----------|
| `ValourResult.Ok()` | 200 | Success, no data |
| `ValourResult.Ok(string)` | 200 | Success with message |
| `Results.Json(object)` | 200 | Success with data |
| `ValourResult.BadRequest(string)` | 400 | Invalid request |
| `ValourResult.InvalidToken()` | 401 | Missing/invalid token |
| `ValourResult.Forbid(string)` | 403 | Insufficient permissions |
| `ValourResult.NotFound(string)` | 404 | Resource not found |
| `ValourResult.NotPlanetMember()` | 403 | Not a planet member |
| `ValourResult.LacksPermission(Permission)` | 403 | Missing specific permission |
| `ValourResult.Problem(string)` | 500 | Server error |

### Example Error Handling

```csharp
public static async Task<IResult> ExampleRouteAsync(...)
{
    var user = await userService.GetCurrentUserAsync();
    if (user is null)
        return ValourResult.InvalidToken();

    var resource = await db.Resources.FindAsync(id);
    if (resource is null)
        return ValourResult.NotFound("Resource not found");

    if (resource.OwnerId != user.Id)
        return ValourResult.Forbid("You do not own this resource");

    try
    {
        await service.Process(resource);
        return ValourResult.Ok("Resource processed");
    }
    catch (Exception ex)
    {
        return ValourResult.Problem($"Failed to process: {ex.Message}");
    }
}
```

---

## Rate Limiting

### Defining Policies

In `RateLimitDefs.cs`:

```csharp
_.AddFixedWindowLimiter("login", options =>
{
    options.PermitLimit = 5;
    options.Window = TimeSpan.FromSeconds(60);
    options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    options.QueueLimit = 2;
});
```

### Applying to Routes

```csharp
[RateLimit("login")]
[ValourRoute(HttpVerbs.Post, "api/users/token")]
public static async Task<IResult> GetTokenRouteAsync(...)
```

---

## Security Best Practices

1. **Always use `[UserRequired]`** for authenticated endpoints
2. **Check planet membership** before planet operations
3. **Check authority hierarchy** before modifying other members
4. **Use `[StaffRequired]`** for admin operations
5. **Apply rate limiting** to sensitive endpoints (login, registration)
6. **Validate all input** before processing
7. **Return appropriate error codes** for debugging without leaking info
