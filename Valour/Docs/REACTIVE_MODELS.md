# Valour Reactive Model System

This document describes Valour's reactive model system, which enables real-time synchronization between server and clients.

## Overview

Valour uses a sophisticated reactive architecture to keep client state synchronized with the server in real-time. The key components are:

1. **ClientModel** - Base class for all synchronized models
2. **ModelStore** - Thread-safe cache with change detection
3. **HybridEvent** - Event system supporting sync and async handlers
4. **Node** - SignalR connection manager
5. **CoreHub** - Server-side SignalR hub

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                           SERVER                                     │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────────────┐   │
│  │   Database   │───▶│   Service    │───▶│   CoreHubService     │   │
│  │              │    │   Layer      │    │  (broadcasts updates)│   │
│  └──────────────┘    └──────────────┘    └──────────┬───────────┘   │
│                                                      │               │
│                                          ┌───────────▼───────────┐   │
│                                          │      CoreHub          │   │
│                                          │   (SignalR Hub)       │   │
│                                          └───────────┬───────────┘   │
└──────────────────────────────────────────────────────┼───────────────┘
                                                       │ SignalR
                                                       │ WebSocket
┌──────────────────────────────────────────────────────┼───────────────┐
│                           CLIENT                     │               │
│                                          ┌───────────▼───────────┐   │
│                                          │        Node           │   │
│                                          │  (SignalR Client)     │   │
│                                          └───────────┬───────────┘   │
│                                                      │               │
│                                          ┌───────────▼───────────┐   │
│  ┌──────────────┐    ┌──────────────┐    │    model.Sync()       │   │
│  │     UI       │◀───│  ModelStore  │◀───│  (applies changes)    │   │
│  │  Components  │    │   (Cache)    │    └───────────────────────┘   │
│  └──────────────┘    └──────────────┘                                │
└─────────────────────────────────────────────────────────────────────┘
```

## ClientModel Hierarchy

```csharp
ClientModel (abstract)
├── Deleted: HybridEvent          // Fired when model is deleted
├── Client: ValourClient          // Reference to owning client
├── Node: Node                    // Reference to SignalR node
└── SetClient(client)             // Sets the owning client

ClientModel<TSelf> : ClientModel
├── Updated: HybridEvent<ModelUpdatedEvent<TSelf>>  // Fired on update
├── Sync(client, flags)           // Syncs model to cache, returns master copy
├── Destroy(client)               // Removes from cache, fires deletion event
├── AddToCache(flags)             // Abstract: adds to appropriate ModelStore
└── RemoveFromCache(skipEvents)   // Abstract: removes from ModelStore

ClientModel<TSelf, TId> : ClientModel<TSelf>
├── Id: TId                       // Model identifier
├── CreateAsync()                 // POST to server
├── UpdateAsync()                 // PUT to server
└── DeleteAsync()                 // DELETE from server
```

### Curiously Recurring Template Pattern (CRTP)

The generic constraints use CRTP for type safety:

```csharp
public abstract class ClientModel<TSelf, TId> : ClientModel<TSelf>
    where TSelf : ClientModel<TSelf, TId>
    where TId : IEquatable<TId>
```

This allows methods to return the correct concrete type without casting.

## ModelStore

`ModelStore<TModel, TId>` is a thread-safe collection that:
- Maintains dual indexes (List for ordering, Dictionary for O(1) lookup)
- Detects changes between existing and incoming models
- Fires events on modifications
- Uses locks to protect against concurrent access from SignalR threads

### Events

```csharp
Changed          // Fires for ANY change
ModelsSet        // Entire collection replaced
ModelsCleared    // Collection cleared
ModelsReordered  // Collection sorted

ModelAdded       // Single item added
ModelUpdated     // Single item updated
ModelDeleted     // Single item removed
```

### Thread Safety

All operations acquire `SyncLock` before modifying collections:

```csharp
lock (SyncLock)
{
    List.Add(model);
    IdMap[model.Id] = model;
}
// Events fired OUTSIDE lock to prevent deadlocks
ModelAdded?.Invoke(addedEvent);
```

### Change Detection

`HandleChanges()` uses compiled property getters/setters for performance:

```csharp
// Compiled at startup, zero reflection at runtime
var getters = ModelUpdateUtils.ModelGetterCache[type];
var setters = ModelUpdateUtils.ModelSetterCache[type];

for (var i = 0; i < properties.Length; i++)
{
    var oldValue = getters[i](existing);
    var newValue = getters[i](updated);

    if (ValuesDiffer(oldValue, newValue))
    {
        changes[prop.Name] = new Change<T>(oldValue, newValue);
        setters[i](existing, newValue);  // In-place update
    }
}
```

### SortedModelStore

Extends ModelStore for auto-sorted collections:

```csharp
public class SortedModelStore<TModel, TId> : ModelStore<TModel, TId>
    where TModel : ClientModel<TModel, TId>, ISortable
```

Automatically repositions items when their sort position changes.

## HybridEvent

Custom event system supporting both synchronous and asynchronous handlers:

```csharp
// Subscribe with sync handler
channel.Updated += (e) => Console.WriteLine("Updated!");

// Subscribe with async handler
channel.Updated += async (e) => await UpdateUIAsync(e);

// Invoke runs sync handlers immediately, then async in parallel
channel.Updated.Invoke(eventData);
```

### Features

- Object pooling for handler lists (reduces GC pressure)
- Double-checked locking for thread-safe initialization
- Supports `+=` and `-=` operators

### Important Notes

- Async handlers run fire-and-forget via `_ = InvokeAsyncHandlers(data)`
- Don't store references to event data in async handlers (may be pooled)

## Node (SignalR Client)

Each `Node` represents a connection to a server instance:

```csharp
public class Node
{
    public HubConnection HubConnection { get; }

    // Track subscriptions for reconnection
    private ConcurrentDictionary<long, byte> _realtimePlanets;
    private ConcurrentDictionary<long, byte> _realtimeChannels;
}
```

### Connection Flow

1. `InitializeAsync()` - Sets up HttpClient and headers
2. `SetupRealtimeConnection()` - Connects SignalR
3. `AuthenticateSignalR()` - Authenticates with token
4. `ConnectToUserChannel()` - Joins user-specific group
5. `HookSignalREvents()` - Registers model update handlers

### Event Hooking

Uses reflection at startup to register handlers for all ClientModel types:

```csharp
HubConnection.On<TModel, int>($"{typeName}-Update", OnModelUpdate<TModel>);
HubConnection.On<TModel>($"{typeName}-Delete", OnModelDelete<TModel>);
```

### Reconnection

On reconnect, the Node:
1. Re-authenticates
2. Rejoins user channel
3. Rejoins all previously connected planets
4. Rejoins all previously connected channels

## Server-Side: CoreHub

SignalR hub managing real-time connections:

```csharp
public class CoreHub : Hub
{
    // Group naming:
    // "u-{userId}"    - User-wide events
    // "p-{planetId}"  - Planet-wide events
    // "c-{channelId}" - Channel-specific events

    public Task<TaskResult> JoinPlanet(long planetId);
    public Task<TaskResult> JoinChannel(long channelId);
    public Task LeavePlanet(long planetId);
    public Task LeaveChannel(long channelId);
}
```

### CoreHubService

Broadcasts updates to relevant groups:

```csharp
public void NotifyPlanetItemChange<T>(long planetId, T model, int flags = 0)
{
    _hub.Clients.Group($"p-{planetId}")
        .SendAsync($"{typeof(T).Name}-Update", model, flags);
}
```

## Update Flow Example

When a user edits a channel name:

```
1. Client calls channel.UpdateAsync()
2. Server validates and saves to database
3. CoreHubService.NotifyPlanetItemChange<Channel>(planetId, channel)
4. SignalR broadcasts "Channel-Update" to group "p-{planetId}"
5. Client Node receives event, calls channel.Sync(client)
6. channel.SetClient(client) sets ownership
7. channel.AddToCache() adds to Client.Cache.Channels
8. ModelStore.Put() detects changes via HandleChanges()
9. Changes applied in-place to existing cached instance
10. ModelUpdated event fired
11. UI components subscribed to event update display
```

## Best Practices

### Subscribing to Updates

```csharp
// Subscribe to specific model updates
channel.Updated += OnChannelUpdated;

// Subscribe to store-wide changes
Client.Cache.Channels.ModelUpdated += OnAnyChannelUpdated;
```

### Checking What Changed

```csharp
void OnChannelUpdated(ModelUpdatedEvent<Channel> e)
{
    if (e.Changes.On(x => x.Name, out var oldName, out var newName))
    {
        Console.WriteLine($"Name changed: {oldName} -> {newName}");
    }
}
```

### Cleanup

Always unsubscribe when done:

```csharp
public void Dispose()
{
    channel.Updated -= OnChannelUpdated;
    Client.Cache.Channels.ModelUpdated -= OnAnyChannelUpdated;
}
```

## Key Files

| File | Purpose |
|------|---------|
| `Valour/Sdk/ModelLogic/ClientModel.cs` | Base model classes |
| `Valour/Sdk/ModelLogic/ModelStore.cs` | Thread-safe cache |
| `Valour/Sdk/ModelLogic/ModelChange.cs` | Change tracking |
| `Valour/Sdk/ModelLogic/ModelUpdateUtils.cs` | Compiled reflection |
| `Valour/Sdk/Nodes/Node.cs` | SignalR client |
| `Valour/Shared/Utilities/HybridEvent.cs` | Event system |
| `Valour/Server/Hubs/CoreHub.cs` | SignalR server hub |
| `Valour/Server/Services/CoreHubService.cs` | Update broadcasting |

## Thread Safety Considerations

- **ModelStore**: All operations are synchronized with `SyncLock`
- **HybridEvent**: Uses separate locks for sync/async handler lists
- **Node**: Uses `ConcurrentDictionary` for subscription tracking
- Events are fired **outside** locks to prevent deadlocks
