# Demos (GOTV)

When GOTV is enabled (`tv_enable 1`, from the launch options or any cfg), MatchZy records a demo of every live map.

| Setting | Default | Purpose |
|---|---|---|
| `matchzy_demo_path` | `demos/` | Folder relative to `csgo/`. |
| `matchzy_demo_name_format` | `{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}` | File name. Also `{MAPNUMBER}`. |

## Reliable recording

Demo recording in CS2 fails silently in several ways (a `mp_restartgame` killing the recording, another plugin changing map, dynamic CSTV removing the bot). This fork guards against them:

- Recording starts on the first round after the go-live restart, in match, scrim and hill (about a second after LIVE).
- The file is checked to be **growing** on disk, not just to exist, and it is watched for the whole map. A stalled recording is restarted into a new file (up to three attempts).
- A restarted recording never overwrites an earlier file: it gets `_part2`, `_part3`, ... and every part of the map is uploaded.
- `tv_record_immediate` is used, so a demo survives a server crash.
- If the CSTV bot is missing when recording should start (kicked by a `bot_quota` change, for example), MatchZy restarts GOTV with `tv_enable 0` / `tv_enable 1`. That only works with `tv_enable_dynamic 1`, which makes a `tv_enable` change take effect right away instead of on the next map. Add `+tv_enable_dynamic 1` to the launch options.
- Practice bots never take the CSTV slot.
- "CSTV is recording this match." is only shown in chat once the file exists. If recording fails, chat says so.
- When the map ends, chat says the recording is finished, with the demo file name, and whether it will be uploaded. It also says so when the recording stops early (`.endmatch`, a reset or a map change).
- Every start, confirmation, retry and stop is written to the server log.

## Uploading

Set an upload URL from a private cfg (it may contain a token):

```text
matchzy_demo_upload_url "https://example.com/upload"
matchzy_demo_upload_header_key "Authorization"
matchzy_demo_upload_header_value "Bearer <token>"
```

The upload starts shortly after the map ends. Two modes:

| Mode | Request |
|---|---|
| Default | `POST` with the demo as body, plus `MatchZy-FileName`, `MatchZy-MatchId`, `MatchZy-MapNumber`, `MatchZy-RoundNumber` headers (also sent as `Get5-*`). |
| S3 (`matchzy_demo_upload_s3 true`) | `PUT` with the raw `.dem` as body and only your custom header, for S3-compatible presigned URLs. Sign the URL with `Content-Type: application/octet-stream`. |

After each upload a `demo_upload_ended` event reports the file name and whether it succeeded.
