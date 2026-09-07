# Roles and permissions

A planet's roles determine what members may do and the order in which roles appear.
A role has a planet ID, name, color, position, administrator flag, permission fields,
and a membership bit index. Position and bit index serve different purposes:
reordering a role changes its authority without changing who belongs to it.

## Position and authority

Lower position values mean greater authority. The shared role contract computes
role authority as `uint.MaxValue - Position - 1`, with `uint.MaxValue` position
explicitly returning zero. The planet owner has a separate
owner authority check. Ordinary role editing and deletion require Manage Roles
and authority greater than the target role. Administrator status has additional
checks when creating, changing, or deleting administrator roles.

Channel permissions are evaluated through the permission service using roles,
permission nodes, and the channel hierarchy. Role order is not a general rule that
copies every permission from one role to the next. Use
`PlanetPermissionService` and the member/channel service methods for decisions.

## Default role

Each planet has one default role. Its `IsDefault` flag cannot be changed, and the
role cannot be deleted. It supplies the planet's baseline role and stays below
ordinary roles in the hierarchy.

When `PlanetRoleService.CreateAsync` creates a role, it places it after the largest
non-default position, or at zero if there are no non-default roles. If necessary,
it moves the default role below that position. It persists and broadcasts both
changes. Position cannot be changed through the ordinary role update route; use
the reorder route.

## Membership flags

Each role receives a `FlagBitIndex` from 0 through 255 within its planet. A member
stores role membership in four 64-bit fields, `RoleMembership0` through
`RoleMembership3`. Index division by 64 chooses the field, and the remainder
chooses its bit. The system therefore has 256 role slots per planet.

Role creation selects the first unused bit index. Deletion clears that bit from
members before it becomes available for another role. A reused bit must not grant
the replacement role to members of the deleted role.

## Persistence, caches, and events

The server keeps the planet's roles and members in `HostedPlanet`. Role services
save database changes, update that cache, and notify connected clients through
`CoreHubService`. The SDK stores roles in `SortedModelStore<PlanetRole, long>`,
which orders them by their sort position and raises model/store events.

Role deletion removes permission nodes, clears member flags, and deletes the role
inside a database transaction. It then reloads affected tracked members, refreshes
the hosted member cache, removes the cached role, and recalculates permissions.
Members receive updated model data, connected clients have their channel access
reconciled, and the role deletion is broadcast.

Model notifications use `PlanetRole-Update` and `PlanetRole-Delete`. The SDK applies
updates to cached instances so components holding a role reference see its current
values. Components that display permission-dependent controls must subscribe to
both role changes and the relevant member's role membership changes.

## HTTP routes

The paths below match `PlanetRoleApi`, `PlanetApi`, and `PlanetMemberApi`.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/planets/{id}/roles` | List roles |
| GET | `/api/planets/{id}/roles/ids` | List role IDs |
| GET | `/api/planets/{id}/roles/counts` | Read role member counts |
| GET | `/api/planet/{planetId}/roles/{roleId}` | Read a role |
| POST | `/api/planet/{planetId}/roles` | Create a role |
| PUT | `/api/planet/{planetId}/roles/{roleId}` | Update a role |
| DELETE | `/api/planet/{planetId}/roles/{roleId}` | Delete a role |
| GET | `/api/planet/{planetId}/roles/{roleId}/nodes` | Read permission nodes |
| POST | `/api/planets/{planetId}/roles/order` | Reorder roles |
| POST | `/api/planets/{planetId}/members/{memberId}/roles/{roleId}` | Assign a role |
| DELETE | `/api/planets/{planetId}/members/{memberId}/roles/{roleId}` | Remove a role assignment |

## Source and tests

- `Valour/Server/Services/PlanetRoleService.cs`: role creation, updates, and deletion.
- `Valour/Server/Services/PlanetPermissionService.cs`: permission evaluation and access updates.
- `Valour/Sdk/ModelLogic/ModelStore.cs`: client storage and sorting.
- `Valour/Shared/Models/ISharedPlanetRole.cs`: shared role contract.
- `Valour/Shared/Authorization/`: permission definitions.
- `Valour/Tests/Services/PlanetPermissionsServiceTests.cs`: permission behavior.

The village browser presence suite also verifies live role assignment and deletion,
including a replacement role reusing a freed membership bit. See
[the browser test guide](../Tests/Browser/README.md).
