# EQClassic rewrite (C# server, Unity client)

Target architecture, protocol choice and milestones: [`docs/architecture-rewrite.md`](../docs/architecture-rewrite.md).

| Project | What it is |
|---|---|
| `src/Shared` | Message contracts and codec (`Protocol/`, `Login/`), client library (`Client/LoginClient`), zone collision mesh from Lantern exports (`World/`), legacy Trilogy credential block (`Legacy/`). Targets `net10.0` and `netstandard2.1` for Unity 2021.3: no newer APIs here. |
| `src/Server` | Login server on LiteNetLib (`Login/`), accounts (`Accounts/`), zone simulation bricks (`Zone/`). |
| `src/Cli` | Command-line client: encrypted login, world list, world key. |
| `tests/Tests` | xUnit tests, including UDP end-to-end tests on localhost; MariaDB tests run when `EQC_REWRITE_TEST_DB` is set. |

```sh
dotnet build && dotnet test
dotnet run --project src/Server -- --port 5999   # test account: test / test; --db "<connection string>" for login_accounts
dotnet run --project src/Cli -- 127.0.0.1 test test --fingerprint <printed by the server>
```

Login behaviour mirrors `LS/Login` (see `LoginServiceTests`): keep them in step when either changes.
