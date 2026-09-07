# Self-hosted voice with LiveKit

Valour supports Cloudflare RealtimeKit and LiveKit for audio and video. This guide
configures the instance-wide LiveKit provider using the Compose overlay in the
repository. Without a configured provider, instance voice is unavailable.

## Connections

The browser obtains a join token from Valour, then connects to the LiveKit server.
Caddy terminates the public signalling WebSocket's TLS connection and proxies it
to LiveKit on port 7880. Audio and video travel directly to LiveKit over UDP 7882
or the TCP 7881 fallback, so those media ports must be reachable independently of
Caddy.

The Valour server signs participant tokens and uses LiveKit's room API for
moderation and cleanup. In this bundle it reaches that API at
`http://livekit:7880` over the Compose network.

## Configure DNS and ports

Point `voice.your-domain.example` at the machine running the stack, in addition
to the application domain. Allow these inbound ports:

| Port | Protocol | Purpose |
| --- | --- | --- |
| 80 | TCP | Caddy HTTPS setup for the application and voice domain |
| 443 | TCP | HTTPS and signalling WebSocket |
| 7882 | UDP | Primary media traffic |
| 7881 | TCP | Media fallback |

`selfhost/livekit.yaml` sets `use_external_ip: true` for public-address discovery.
If discovery does not identify the reachable address, configure `node_ip` in that
file. Networks that block direct media may require a TURN relay with its own port
and certificate configuration.

## Configure credentials

Generate a random secret by running `openssl rand -hex 32`. Copy its output into
`.env` as a literal value. Compose does not execute shell commands written in
`.env`.

```dotenv
VOICE_PROVIDER=livekit
VOICE_LIVEKIT_URL=wss://voice.your-domain.example
VOICE_LIVEKIT_API_KEY=valour
VOICE_LIVEKIT_API_SECRET=PASTE_GENERATED_SECRET_HERE
```

Use the same key and secret for Valour and LiveKit. The overlay supplies that pair
to LiveKit through `LIVEKIT_KEYS`. Valour's server-facing API URL defaults to
`http://livekit:7880`; set `VOICE_LIVEKIT_API_URL` only if that address differs in
your deployment.

Uncomment the `voice.{$VALOUR_DOMAIN}` site block in `selfhost/Caddyfile`. Then run:

```sh
docker compose -f docker-compose.yml -f docker-compose.voice.yml up -d
```

The overlay requires the API key and secret. After changing environment settings,
run the Compose command again so affected containers are recreated with them.

## Verify the provider

Check `https://your-domain.example/.well-known/valour-instance`. A configured
LiveKit instance reports `voice: true`, `voiceProvider: livekit`, and its public
`voiceEndpoint`. Join a call with two accounts and verify microphone, camera,
screen sharing, leaving, and reconnecting.

`Voice:Provider` explicitly selects `livekit` or `realtimekit`. With no explicit
selection, Valour selects LiveKit when it is configured and RealtimeKit is not;
otherwise it selects RealtimeKit. The selected provider still needs its credentials
to report itself configured. RealtimeKit uses the `Cloudflare:Realtime*` settings.

An enabled planet-specific LiveKit configuration takes precedence for that
planet's calls. Direct and group calls use the instance provider. The manifest
reports instance capability, not every planet's individual voice configuration.

## Client behavior

The call UI supports joining, muting, camera, screen sharing, participant controls,
and device selection through the provider integrations. LiveKit uses browser
`noiseSuppression`, `echoCancellation`, and `autoGainControl` constraints for audio
processing. Its video elements attach through the SDK so adaptive streaming can
respond to their displayed size.

Call capacity depends on stream count, video resolution, available bandwidth, and
host resources. Measure the expected workload instead of inferring capacity from
a successful two-person call.

## Troubleshooting

If signalling connects but media does not arrive, check UDP 7882 and TCP 7881,
NAT forwarding, and the public address advertised by LiveKit. If signalling fails,
check voice DNS, Caddy's site block, and certificate logs. If Valour reports an
unconfigured provider, check that the Compose service receives the `Voice__LiveKit*`
settings and recreate it after configuration changes.

The [local media suite](../../Valour/Tests/Browser/README.md#local-media-regression)
exercises the application with an isolated local SFU and synthetic capture.
Public deployment still needs verification through the actual provider endpoint
and intended client devices.
