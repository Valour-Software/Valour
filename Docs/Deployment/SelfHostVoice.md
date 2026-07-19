# Self-hosted voice & video (LiveKit)

Valour's managed deployment uses Cloudflare RealtimeKit for voice/video. Self-hosters
can instead run their **own** [LiveKit](https://livekit.io) SFU so that call media
never leaves their infrastructure. Both backends are feature-equivalent from the
client's point of view; the server picks one via config.

By default voice is **disabled** (no backend configured). This guide enables the
self-hosted LiveKit backend using the bundled Compose overlay.

## How it fits together

```
        wss (signalling, TLS via Caddy)          direct UDP/TCP (media)
Browser ───────────────────────────► Caddy ──► LiveKit ◄──────────────────── Browser
   │                                   :443      :7880          :7882/udp, :7881/tcp
   │  api/voice/token  (join token)                  ▲
   └──────────────────────────────► Valour ──────────┘
                                     server   room admin over the compose network
```

- **Signalling** is a WebSocket the browser opens to `voice.$VALOUR_DOMAIN`; Caddy
  terminates TLS and proxies it to the LiveKit container. 
- **Media** (the actual audio/video) flows **directly** between each browser and the
  host over UDP `7882` (falling back to TCP `7881`). It does *not* pass through Caddy,
  so those ports must be reachable from the internet.
- The **Valour server** mints LiveKit access tokens locally (no round trip) and talks
  to LiveKit's room API over the internal compose network for moderation/cleanup.

## Steps

### 1. DNS

Add an A/AAAA record `voice.$VALOUR_DOMAIN` pointing at the host (in addition to the
existing record for `$VALOUR_DOMAIN`).

### 2. Firewall / NAT — the #1 thing to get right

Forward these host ports to the machine running the stack:

| Port | Protocol | Purpose |
|------|----------|---------|
| 7882 | UDP | Primary media path. **Required.** |
| 7881 | TCP | Media fallback for networks that block UDP. |
| 443  | TCP | Signalling websocket (already open for the app). |

If the host is behind NAT (most cloud VPSes are), LiveKit discovers its public IP via
STUN automatically (`use_external_ip: true` in `selfhost/livekit.yaml`). If discovery
fails, set `node_ip:` explicitly in that file.

For clients behind very restrictive firewalls, enable LiveKit's embedded TURN-over-TLS
relay (`turn.enabled` in `selfhost/livekit.yaml`); it needs its own port and certificate.

### 3. Configure `.env`

```bash
VOICE_PROVIDER=livekit
VOICE_LIVEKIT_URL=wss://voice.your-domain.example
VOICE_LIVEKIT_API_KEY=valour
VOICE_LIVEKIT_API_SECRET=$(openssl rand -hex 32)   # keep it long (>= 32 chars) and secret
```

The server signs join tokens with the key/secret; the same pair is handed to the
LiveKit container via `LIVEKIT_KEYS`. `VOICE_LIVEKIT_API_URL` defaults to
`http://livekit:7880` (the internal address) and rarely needs changing.

### 4. Enable the Caddy voice block

Uncomment the `voice.{$VALOUR_DOMAIN}` block at the bottom of `selfhost/Caddyfile`.

### 5. Start the stack with the voice Compose overlay

```bash
docker compose -f docker-compose.yml -f docker-compose.voice.yml up -d
```

The manifest at `https://$VALOUR_DOMAIN/.well-known/valour-instance` should now report
`"voice": true`, `"voiceProvider": "livekit"`, and the `voiceEndpoint`. Voice/video
channels become available in the client automatically.

## Provider selection logic

The server resolves the active backend from config:

- `Voice__Provider=livekit` → LiveKit (this guide).
- `Voice__Provider=realtimekit` → Cloudflare RealtimeKit (needs the `Cloudflare__Realtime*` keys).
- unset → auto: LiveKit **only if** it is configured and RealtimeKit is not; otherwise
  RealtimeKit. So the managed deployment is never silently switched.

## Feature parity & minor differences

Everything the client does — join/leave, mute/unmute, camera, screen share (with audio),
active-speaker highlighting, device switching, moderation (server-side kick, client-relayed
mute) — works identically on both backends.

- **Noise suppression / echo cancellation:** on LiveKit these use the browser's built-in
  `noiseSuppression` / `echoCancellation` / `autoGainControl` constraints (enabled by
  default). RealtimeKit's managed enhancement isn't present; browser-level processing is
  what you get, which is fine for the vast majority of calls.
- **Scaling:** a single LiveKit container comfortably handles small/medium communities on
  a modest VPS. Voice is cheap; video/screen-share bandwidth scales with how many streams
  are being forwarded. For large deployments, see LiveKit's distributed/SFU-mesh docs.

## Troubleshooting

- **Connects then no audio/video, or "connecting" hangs:** almost always UDP `7882` isn't
  reachable. Verify the port-forward and that `use_external_ip`/`node_ip` resolves to the
  right public address. Confirm TCP `7881` is open as a fallback.
- **Signalling fails immediately:** check `voice.$VALOUR_DOMAIN` DNS and that the Caddy
  voice block is uncommented and got a certificate (`docker compose logs caddy`).
- **Server logs "LiveKit is not configured":** the `Voice__LiveKit*` env vars didn't reach
  the `valour` container — confirm they're in `.env` and re-run compose.
