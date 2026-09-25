# eqbot — headless Trilogy test client

A small Linux client that speaks the Trilogy protocol to the EQClassic servers, for smoke tests
and CI. It reuses the servers' own UDP layer (`Common/Source/EQPacket*.cpp`: sequencing, acks,
resends, fragments), since the wire format is the same in both directions.

```sh
cmake -S . -B build -DEQC_BUILD_SERVERS=OFF && cmake --build build --target eqbot   # needs libssl-dev
build/bin/eqbot login  <login-host> <user> <password> [port=5999]
build/bin/eqbot create <login-host> <user> <password> <character>
build/bin/eqbot play   <login-host> <user> <password> <character>
```

| Command | What it checks |
|---|---|
| `login` | Credentials (DES-encrypted like the client), session id, banner, server list, world status, session key |
| `create` | `login`, world character list, name approval, character creation. Does nothing if the character already exists. |
| `play` | `login`, world, enter world, then the zone handshake: player profile, zone header, spawns, "Enterzone complete". Ends with a clean disconnect. |

The output has one `[ OK ]`/`[FAIL]` line per step, and the exit code is 0 only when every step passed.
`EQBOT_VERBOSE=1` lists the zone packets and `EQBOT_RAW=1` dumps every datagram.

The creation payload (`charcreate_template.inc`) is a packet captured from the real client, a troll
shaman, with its name zeroed. The world server sets the starting items and zone itself.

`ci-e2e.sh <server dir> <eqbot>` is the end-to-end CI job (`.github/workflows/build.yml`, job
`e2e`). It runs MariaDB in Docker, imports the dump and patches, then runs the Release servers
under Wine. The bot then logs in, creates a character and plays twice. The second session catches
the zone failing to log the first one out, which shows up as "Error 1018: active character".
