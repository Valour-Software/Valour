# Client JS tests

Tests for client-side TypeScript that has no C# surface to reach it through -
the village canvas runtime, its texture cache, and the positional voice graph.
These run against the **compiled** `.js` next to each `.ts`, so build the client
first (the csproj compiles TypeScript in place).

They use Node's built-in test runner, so there is no package.json and no
dependency to install.

```bash
dotnet build Valour/Client/Valour.Client.csproj
node --test Valour/Tests/Js/*.test.mjs
```

Browser APIs are stubbed per file rather than through a DOM library: each test
supplies only the handful of `AudioContext`/`Image`/`document` members the code
under test actually touches, which keeps the stubs readable and makes it obvious
when production code starts depending on something new.

Note that `node --test` needs the file glob rather than the directory - passing
the directory makes Node try to resolve it as a module.
