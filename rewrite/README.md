# EQClassic rewrite (C# server, Unity client)

Target architecture, protocol choice and milestones: [`docs/architecture-rewrite.md`](../docs/architecture-rewrite.md).

| Project | What it is |
|---|---|
| `src/Shared` | Message contracts and codec (`Protocol/`, `Login/`), client library (`Client/LoginClient`), zone collision mesh from Lantern exports (`World/`), legacy Trilogy credential block (`Legacy/`). Targets `net10.0` and `netstandard2.1` for Unity 2021.3: no newer APIs here. |
| `src/Server` | Login server on LiteNetLib (`Login/`), accounts (`Accounts/`), zone simulation bricks (`Zone/`). |
| `tests/Tests` | xUnit tests, including UDP end-to-end tests on localhost. |

```sh
dotnet build && dotnet test
dotnet run --project src/Server -- --port 5999   # test account: test / test
```

Login behaviour mirrors `LS/Login` (see `LoginServiceTests`): keep them in step when either changes.
