# Guidance for AI Agents

This file provides guidance for AI coding assistants working on the Valour codebase.

## Documentation

Before making changes to core systems, review the documentation in the `Docs/` directory:

- **`Docs/ReactiveModelSystem.md`** - Explains the ClientModel, ModelStore, HybridEvent, and real-time update architecture. Essential reading before modifying:
  - `Valour/Sdk/ModelLogic/` (ClientModel, ModelStore, ModelChange, etc.)
  - `Valour/Sdk/Nodes/Node.cs`
  - `Valour/Shared/Utilities/HybridEvent.cs`
  - `Valour/Server/Hubs/CoreHub.cs`
  - `Valour/Server/Services/CoreHubService.cs`

## Project Structure

```
Valour/
├── Config/                    # Configuration classes
├── Valour/
│   ├── Client/               # Blazor WASM client (deprecated, being migrated)
│   ├── Client.Blazor/        # New Blazor client
│   ├── Database/             # Entity Framework models and DbContext
│   ├── Sdk/                  # Client SDK (models, services, SignalR client)
│   │   ├── ModelLogic/       # Reactive model system
│   │   ├── Models/           # Client-side model implementations
│   │   ├── Nodes/            # SignalR connection management
│   │   └── Services/         # Client-side services
│   ├── Server/               # ASP.NET Core server
│   │   ├── Api/              # REST API endpoints
│   │   ├── Cdn/              # CDN and file handling
│   │   ├── Hubs/             # SignalR hubs
│   │   └── Services/         # Server-side services
│   └── Shared/               # Shared code between client and server
│       ├── Cdn/              # CDN utilities
│       ├── Models/           # Shared model interfaces
│       └── Utilities/        # HybridEvent, etc.
└── Docs/                     # Architecture documentation
```

## Key Architectural Patterns

### 1. Reactive Model System
Models sync in real-time via SignalR. See `Docs/ReactiveModelSystem.md`.

### 2. Thread Safety
- `ModelStore` uses `SyncLock` for all collection operations
- Events are fired **outside** locks to prevent deadlocks
- `HybridEvent` uses double-checked locking for initialization

### 3. Multi-Node Architecture
Valour supports multiple server nodes. Clients connect to specific nodes based on which planets they're accessing.

## Common Pitfalls

1. **Don't hold locks while firing events** - Can cause deadlocks
2. **ModelStore is thread-safe** - Don't add external locking
3. **HybridEvent async handlers are fire-and-forget** - Don't rely on completion
4. **Don't store event data in async handlers** - May be pooled/recycled

## Testing

```bash
# Build everything
dotnet build

# Run server
cd Valour/Server && dotnet run

# Run tests
dotnet test
```

## Contributing

When modifying core systems:
1. Read relevant documentation in `Docs/`
2. Understand the threading model
3. Maintain existing patterns
4. Update documentation if you change architecture
