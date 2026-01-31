# Valour Role System Documentation

This document provides comprehensive documentation for the Valour role system, covering both server and client implementations.

## Table of Contents

- [Overview](#overview)
- [Position vs Authority](#position-vs-authority)
- [Default Role](#default-role)
- [FlagBitIndex System](#flagbitindex-system)
- [Server Caching](#server-caching)
- [Client Caching](#client-caching)
- [Role Lifecycle](#role-lifecycle)
- [API Endpoints](#api-endpoints)
- [Common Issues](#common-issues)

---

## Overview

Roles in Valour are hierarchical permission containers that determine what members can do within a planet. Each role has:

- **Id**: Unique identifier
- **PlanetId**: The planet this role belongs to
- **Name**: Display name
- **Position**: Determines hierarchy (lower = more authority)
- **IsDefault**: Whether this is the planet's default role
- **IsAdmin**: Whether this role has admin privileges
- **Color**: Hex color code for display
- **FlagBitIndex**: Position in the 256-bit membership flags
- **Permissions**: Various permission fields for different contexts

---

## Position vs Authority

**Key Principle**: Lower position = higher authority.

The authority calculation formula is:

```csharp
Authority = uint.MaxValue - Position - 1
```

This means:
- Position `0` has the highest authority
- Position `N` has lower authority than position `N-1`
- The default role always has the highest position (lowest authority)

### Why This Matters

When comparing roles for permission checks:
1. A member can only modify roles with **higher** position (lower authority) than their highest role
2. Roles are sorted by position for display (lowest position at top)
3. Permission inheritance flows from lower positions to higher positions

---

## Default Role

Every planet has exactly one default role with special characteristics:

- **IsDefault = true**: Cannot be changed
- **Cannot be deleted**: Enforced at the service level
- **Always last in hierarchy**: Has the highest position value
- **Created at planet creation**: Initial position set to `int.MaxValue`
- **Automatically bumped**: When new roles are created, the default role's position is automatically incremented to stay last

### Behavior on New Role Creation

When a new role is created:
1. Find the maximum position of all non-default roles
2. Assign new role position = max + 1 (or 0 if no other roles)
3. If default role position <= new role position, bump default to new role position + 1
4. Notify clients of both changes

---

## FlagBitIndex System

Role membership uses a 256-bit flag system for efficient lookups.

### How It Works

- Each role has a unique `FlagBitIndex` (0-255) within its planet
- Member role membership is stored as four `long` fields: `RoleMembership0`, `RoleMembership1`, `RoleMembership2`, `RoleMembership3`
- Each long stores 64 bits, giving 256 total role slots per planet

### Bit Operations

```csharp
// Check if member has role
public static bool HasRoleByIndex(this PlanetMember member, int index)
{
    var segment = index / 64;
    var bit = index % 64;
    var membership = GetMembershipSegment(member, segment);
    return (membership & (1L << bit)) != 0;
}

// Set role membership
public static void SetRoleFlag(this PlanetMember member, int index, bool value)
{
    var segment = index / 64;
    var bit = index % 64;
    // ... set or clear the bit
}
```

### Index Reuse

When a role is deleted, its `FlagBitIndex` becomes available for reuse. New roles scan for the first free index:

```csharp
var indiceArr = new int[256];
foreach (var r in roles)
    indiceArr[r.FlagBitIndex] = 1;

for (int i = 0; i < indiceArr.Length; i++)
{
    if (indiceArr[i] == 0)
    {
        role.FlagBitIndex = i;
        break;
    }
}
```

---

## Server Caching

### HostedPlanet

The server maintains in-memory caches of planet data in `HostedPlanet` instances.

```csharp
public class HostedPlanet
{
    private readonly SortedServerModelList<PlanetRole> _roles;

    public void UpsertRole(PlanetRole role) => _roles.Upsert(role);
    public void RemoveRole(long roleId) => _roles.Remove(roleId);
    public PlanetRole GetRoleById(long id) => _roles.GetById(id);
    public IReadOnlyList<PlanetRole> GetRoles() => _roles.GetSnapshot();
}
```

### SortedServerModelList

A thread-safe sorted list that:
- Maintains items sorted by `Position` property
- Provides atomic upsert/remove operations
- Returns immutable snapshots for iteration
- Uses `ReaderWriterLockSlim` for thread safety

---

## Client Caching

### SortedModelStore

Clients use `SortedModelStore<T>` for reactive caching:

```csharp
public class SortedModelStore<TModel> where TModel : ClientModel
{
    // Automatic sorting by ISortable.GetSortPosition()
    // Event notifications for UI updates
    // Efficient add/update/remove operations
}
```

### Sorting

Roles implement `ISortable`:

```csharp
public int GetSortPosition() => (int)Position;
```

The store automatically maintains sort order and fires events when items change.

---

## Role Lifecycle

### Creation

1. **API Request**: Client sends role data to `POST /api/planets/{planetId}/roles`
2. **Validation**: Service validates hex color, generates ID
3. **Position Assignment**: Calculates position based on max existing position
4. **FlagBitIndex Assignment**: Finds first free index in 256-bit space
5. **Database Save**: Persists role and potentially updates default role
6. **Cache Update**: Upserts into `HostedPlanet` cache
7. **Client Notification**: SignalR broadcasts change to connected clients

### Reordering

1. **API Request**: Client sends ordered list of role IDs
2. **Validation**: Verifies user has permission to modify all involved roles
3. **Position Update**: Assigns new positions maintaining relative order
4. **Database Save**: Persists all position changes
5. **Cache Update**: Updates each role in cache
6. **Client Notification**: Broadcasts all changes to clients

### Deletion

1. **API Request**: Client sends `DELETE /api/planets/{planetId}/roles/{roleId}`
2. **Validation**: Ensures role is not default
3. **Permission Node Cleanup**: Removes all permission nodes for this role
4. **Member Flag Cleanup**: Clears the role's bit in all member flags
5. **Database Delete**: Removes role record
6. **Cache Update**: Removes from `HostedPlanet` cache
7. **Client Notification**: Broadcasts deletion to clients

---

## API Endpoints

### REST Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/planets/{planetId}/roles` | Get all roles for a planet |
| GET | `/api/planets/{planetId}/roles/{roleId}` | Get a specific role |
| POST | `/api/planets/{planetId}/roles` | Create a new role |
| PUT | `/api/planets/{planetId}/roles/{roleId}` | Update a role |
| DELETE | `/api/planets/{planetId}/roles/{roleId}` | Delete a role |
| POST | `/api/planets/{planetId}/roles/order` | Reorder roles |

### SignalR Events

| Event | Payload | Description |
|-------|---------|-------------|
| `Planet-RoleChange` | `PlanetRole` | Role created or updated |
| `Planet-RoleDelete` | `PlanetRole` | Role deleted |

Clients subscribe to events for their connected planets and update local caches accordingly.

---

## Common Issues

### Position Collision (Fixed)

**Problem**: After reordering roles, creating a new role could result in position collisions with the default role.

**Symptoms**:
- Default role appearing in the middle of the role list
- Non-deterministic sorting behavior
- Inability to reorder or delete roles

**Root Cause**: The old code used `Count()` to determine new role position:

```csharp
// Old (buggy) code
role.Position = (uint)await _db.PlanetRoles.CountAsync(x => x.PlanetId == role.PlanetId && !x.IsDefault);
```

After reordering, the default role might have position `N-1`, but a new role would also get position `N-1` (since count = N).

**Fix**: Use max position instead of count, and bump the default role:

```csharp
// New (fixed) code
var maxNonDefaultPosition = roles.Where(x => !x.IsDefault).Max(x => (uint?)x.Position) ?? 0;
role.Position = roles.Any(x => !x.IsDefault) ? maxNonDefaultPosition + 1 : 0;

if (defaultRole != null && defaultRole.Position <= role.Position)
{
    defaultRole.Position = role.Position + 1;
    _db.PlanetRoles.Update(defaultRole);
}
```

### Cache Synchronization

**Problem**: Client cache gets out of sync with server state.

**Symptoms**:
- Stale role data displayed
- Permission checks using outdated information

**Solutions**:
1. Ensure SignalR connection is healthy
2. On reconnect, refresh planet data
3. Use optimistic updates with server confirmation

### FlagBitIndex Exhaustion

**Problem**: Planet has reached 256 role limit.

**Solution**: This is a hard limit of the system. Planets cannot have more than 256 roles. Consider consolidating roles or using permission nodes for fine-grained control.

---

## Related Files

- `Valour/Server/Services/PlanetRoleService.cs` - Server-side role operations
- `Valour/Server/Hosting/HostedPlanet.cs` - Server-side caching
- `Valour/Client/ModelStore/SortedModelStore.cs` - Client-side caching
- `Valour/Shared/Models/PlanetRole.cs` - Shared role model
- `Valour/Shared/Authorization/` - Permission definitions
