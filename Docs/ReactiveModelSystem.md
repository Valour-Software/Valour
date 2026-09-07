# Reactive models and real-time updates

The SDK keeps a shared set of model instances for the client. When the server
sends an update, the SDK copies changed properties into the cached instance and
notifies subscribers. A component can keep a reference to a channel or role and
respond to changes without replacing that reference after every request.

## How an update reaches the UI

```text
Server service saves a change
    -> CoreHubService sends a model notification
    -> Node receives the SignalR event
    -> ClientModel.Sync selects the model's cache
    -> ModelStore updates the cached instance
    -> model and store events notify subscribers
```

The server chooses the recipients. User groups use `u-{userId}`, planet groups
use `p-{planetId}`, and channel groups use `c-{channelId}`. Joining a group goes
through `CoreHub` authorization; knowing a group or model ID does not grant access.

`Node` registers typed handlers named `{ModelType}-Update` and
`{ModelType}-Delete`. An update carries the model and insertion flags. Deletion
removes the model through its cache implementation and raises deletion events.
HTTP responses also pass through SDK model synchronization where applicable.

## Model ownership and identity

`ClientModel` holds the owning `ValourClient`, resolves its node, and exposes a
`Deleted` event. `ClientModel<TSelf>` adds `Updated`, `Sync`, and cache hooks.
`ClientModel<TSelf, TId>` adds an ID and the create, update, and delete HTTP methods.
The self type parameter lets these methods return the concrete model type.

Create, update, and delete return an unsuccessful `TaskResult` when the ID is
inappropriate for the operation or the owning node is unavailable. Callers display
that result and allow retry after reconnecting. An unavailable community node does
not cause the operation to use the primary node.

`Sync` sets the client, synchronizes submodels, and calls `AddToCache`. Its return
value is the canonical cached instance. Use that returned instance when keeping a
reference; the incoming object may only be a temporary deserialized copy.

Community-node IDs are scoped to their origin. A store key contains both the ID
and an optional scope, and external models derive `CacheScope` from their node.
Two community nodes may use the same numeric ID for different channels or roles.
Use scoped lookups and the model's node when handling those objects. Unscoped
store overloads address the hub's model space.

`Node` validates external model data before synchronizing it. Community nodes may
supply authorized planet-scoped models, but cannot replace hub account state or
models belonging to another community origin. See
[Federation architecture](FederationArchitecture.md) for that trust boundary.

## Model stores

`ModelStore<TModel, TId>` maintains a list for iteration and a dictionary for
lookup. `Put` inserts a new object or updates an existing one in place. Change
detection uses property metadata and cached getter/setter delegates from
`ModelUpdateUtils`. Properties marked `IgnoreRealtimeChanges` are excluded.

A change record contains the old and new values for each changed property.
`SortedModelStore<TModel, TId>` also tracks sort-position changes and repositions
the model before publishing its events. Models in that store implement
`ISortable`.

| Event | Meaning |
| --- | --- |
| `ModelAdded` | A model was inserted |
| `ModelUpdated` | Properties of a cached model changed |
| `ModelDeleted` | A model was removed |
| `ModelsSet` | The collection was replaced or explicitly reported as set |
| `ModelsCleared` | The collection was cleared |
| `ModelsReordered` | An explicit store sort completed |
| `Changed` | A store operation raised a change event |

An individual model's update event runs before the store's update and general
change events. `SkipEvents` suppresses notifications, `SkipSorting` suppresses
insertion sorting, and `Batched` combines those flags. A caller using these flags
must arrange the final sorting and notification its consumers need.

## Locks and snapshots

The store protects its list and dictionary with `SyncLock`. Enumeration copies
the list while holding that lock and iterates the snapshot after releasing it.
This protects the collection from concurrent edits, but the models in the snapshot
are still shared mutable objects. A snapshot is not a frozen copy of their fields.

Store and model events are invoked outside collection locks. Handlers can call
other stores or services, so invoking them under a lock risks deadlock. Use the
store's operations rather than adding external locks around normal cache access.
Separate operations such as a lookup followed by an update do not become an atomic
transaction merely because each operation is synchronized.

## HybridEvent

`HybridEvent<T>` accepts both `Action<T>` and `Func<T, Task>` handlers. The
parameterless `HybridEvent` has the corresponding parameterless forms. Handler
lists are initialized under a lock. Invocation copies the lists under their own
locks, then calls handlers after releasing those locks.

`Invoke` runs synchronous handlers first and starts asynchronous handlers without
waiting for them to finish. The asynchronous invocation observes their tasks and
logs failures. Calling `Invoke` therefore does not establish that asynchronous
work has completed. Use an explicitly awaited method when later work depends on
completion.

The event implementation pools handler snapshots and task lists. Model change
dictionaries are also pooled. Read the values a handler needs before handing work
to another task, and keep those values rather than retaining a change dictionary.
Do not dispose a shared change record while another subscriber may still use it.

## Subscribing from components

Subscribe with a named handler so disposal can remove the same delegate:

```csharp
channel.Updated += OnChannelUpdated;

void OnChannelUpdated(ModelUpdatedEvent<Channel> update)
{
    if (update.Changes is not null &&
        update.Changes.On(x => x.Name, out var oldName, out var newName))
    {
        Console.WriteLine($"Channel renamed from {oldName} to {newName}");
    }
}
```

Remove the handler with `channel.Updated -= OnChannelUpdated` when the component
stops observing that channel. Dispose events owned by the component, but do not
dispose a model's shared event just because one subscriber is leaving.

A UI handler must also request rendering through the component's normal render
path. SignalR callbacks can arrive off the UI context. Components inheriting
`ControlledRenderComponentBase` explicitly request updates with `ReRender()`.
Use Blazor `EventCallback` for parent/child component parameters.

## Staged planet messages

`PlanetMessageWorker` holds accepted planet messages in memory until a database
flush. If a batch fails, it clears the failed entity tracking state and retries
each message independently. Only confirmed saves leave staging. Individual
failures remain staged for another attempt, so one invalid message cannot discard
other accepted messages. Existing database IDs are recognized when retrying after
a lost commit acknowledgement. Staging is not durable across process loss, and
repeated failures require investigation of the underlying data or service error.

## Connections and recovery

Node initialization serializes realtime setup and reports success only after
authentication and the applicable user-group join succeed. Joins and leaves
return unsuccessful results when the connection drops during invocation. The
heartbeat awaits a bounded, cancellable invocation so timed-out calls cannot
leave an unobserved task running.

Each `Node` owns HTTP access and a SignalR connection to a server. The SDK tracks
planet and channel subscriptions so reconnect can authenticate again, rejoin the
user group where applicable, and restore those subscriptions. Village presence
also restores its current map after node authentication.

A connection can be restored while a particular subscription is denied or fails.
Keep failures visible to the caller, and refresh authoritative data when a feature
requires it. Explicit scene and forced channel refreshes bypass the node's short
HTTP response cache so they receive the server's current state.

The SDK notification service also returns snapshots of its unread list and source
lookup. These snapshots preserve collection membership during incoming updates;
their notification models remain canonical mutable instances. Notification events
run after the service releases its collection lock.

## Source files

- `Valour/Sdk/ModelLogic/ClientModel.cs`: model ownership, synchronization, and CRUD.
- `Valour/Sdk/ModelLogic/ModelStore.cs`: stores, events, scope keys, and sorting.
- `Valour/Sdk/ModelLogic/ModelChange.cs`: typed property changes.
- `Valour/Sdk/ModelLogic/ModelUpdateUtils.cs`: cached property accessors.
- `Valour/Sdk/Nodes/Node.cs`: transport, validation, and reconnection.
- `Valour/Shared/Utilities/HybridEvent.cs`: handler invocation and pooling.
- `Valour/Server/Hubs/CoreHub.cs`: authenticated real-time methods.
- `Valour/Server/Services/CoreHubService.cs`: notification delivery.
