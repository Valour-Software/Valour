# Self-Hosting

This folder contains the deployment package for running Valour yourself.

The goal of this package is operational simplicity:

- one main config file: [`.env.example`](./.env.example)
- one stack file: [`compose.yaml`](./compose.yaml)
- one Nginx template: [`nginx/templates/default.conf.template`](./nginx/templates/default.conf.template)

## What This Package Does

This stack starts four containers:

- `dotnetapp`: the Valour server, which also serves the web app assets
- `postgres`: the primary database
- `redis`: the cache / coordination store the current server still expects
- `nginx`: the HTTPS reverse proxy in front of the app

This package is intentionally scoped to:

- single-host web deployment
- single-node hosting
- one reverse proxy included

## Before You Start

You need:

- a Linux server or VM with Docker and Docker Compose installed
- a domain name you control
- a DNS record pointing that domain name to your server
- ports `80` and `443` open to the internet
- an SSL/TLS certificate and private key for your hostname

For the easiest setup, use one hostname such as `chat.example.com`.
That single hostname can serve:

- the web app at `/`
- the API at `/api/...`
- SignalR at `/hubs/core`

## Files In This Folder

- [`compose.yaml`](./compose.yaml): starts the full stack
- [`.env.example`](./.env.example): copy this to `.env` and edit it
- [`nginx/templates/default.conf.template`](./nginx/templates/default.conf.template): Nginx config template
- [`nginx/ssl`](./nginx/ssl): suggested place to put your certificate files

## Quick Start

### 1. Enter the self-host folder

```bash
cd deploy/self-host
```

### 2. Create your operator config

Copy the sample env file:

```bash
cp .env.example .env
```

### 3. Edit `.env`

Open `.env` in a text editor and set at least these values:

- `SERVER_NAMES`: the hostname Nginx should answer for, for example `chat.example.com`
- `PUBLIC_API_BASE_URL`: the public HTTPS URL of this deployment, for example `https://chat.example.com`
- `POSTGRES_PASSWORD`: a strong database password
- `NODE_NAME`: a name for this node
- `NODE_KEY`: a long random secret
- `TLS_CERT_PATH`: the path to your certificate file
- `TLS_KEY_PATH`: the path to your private key file

If you already have `openssl`, these commands will generate strong random values:

```bash
openssl rand -hex 24
openssl rand -hex 32
```

### 4. Choose your certificate setup

You have two common ways to handle HTTPS:

- use a normal public certificate installed on your server
- use Cloudflare as a proxy and install a Cloudflare Origin CA certificate on your server

#### Option A: Public certificate on your server

Use this option if:

- visitors will connect directly to your server hostname
- you are not using Cloudflare proxying for this hostname
- you want the certificate to be trusted directly by browsers

Typical sources for this kind of certificate:

- Let's Encrypt
- your hosting provider
- any public certificate authority

Common Let's Encrypt file paths on Linux look like:

- `/etc/letsencrypt/live/chat.example.com/fullchain.pem`
- `/etc/letsencrypt/live/chat.example.com/privkey.pem`

Once you have the certificate and private key files:

1. Put them in one of these places:
   - `deploy/self-host/nginx/ssl/server.crt`
   - `deploy/self-host/nginx/ssl/server.key`
2. Or store them somewhere else and update:
   - `TLS_CERT_PATH`
   - `TLS_KEY_PATH`
3. Make sure the hostname on the certificate matches `SERVER_NAMES`.

Example:

```dotenv
TLS_CERT_PATH=/etc/letsencrypt/live/chat.example.com/fullchain.pem
TLS_KEY_PATH=/etc/letsencrypt/live/chat.example.com/privkey.pem
```

#### Option B: Cloudflare-proxied hostname with Cloudflare Origin CA

Use this option if:

- your hostname is proxied through Cloudflare
- you want Cloudflare to handle the public edge certificate
- you want your server to use a Cloudflare-issued origin certificate

Recommended Cloudflare flow:

1. In Cloudflare DNS, create your hostname and make sure it is proxied through Cloudflare.
2. In Cloudflare, go to `SSL/TLS` -> `Origin Server`.
3. Create an Origin CA certificate for your hostname.
4. Download the certificate and private key from Cloudflare.
5. Save them to:
   - `deploy/self-host/nginx/ssl/server.crt`
   - `deploy/self-host/nginx/ssl/server.key`
6. In Cloudflare, go to `SSL/TLS` -> `Overview`.
7. Set the encryption mode to `Full (strict)`.

Important:

- Cloudflare Origin CA certificates are for the connection between Cloudflare and your server.
- They are not meant to be trusted directly by browsers.
- If you disable Cloudflare proxying later, switch to a normal public certificate first.

### 5. Place your certificate files

If you want to use the default paths from `.env`, place your certificate and key here:

- `deploy/self-host/nginx/ssl/server.crt`
- `deploy/self-host/nginx/ssl/server.key`

If your certificate files live somewhere else, update `TLS_CERT_PATH` and `TLS_KEY_PATH` in `.env`.

### 6. Point DNS at your server

Create a DNS record for your hostname so it points at your server's public IP address.

Example:

- `chat.example.com` -> your server IP

If you are using Cloudflare proxying, the DNS record should be proxied in Cloudflare.

### 7. Start the stack

```bash
docker compose pull
docker compose up -d
```

### 8. Check that everything started

```bash
docker compose ps
docker compose logs --tail=100 dotnetapp nginx postgres redis
```

### 9. Test the deployment

Open these URLs in your browser:

Web app:

```text
https://your-domain.example/
```

API health check:

```text
https://your-domain.example/api/ping
```

If the stack is working:

- the root URL should load the Valour web app
- `/api/ping` should respond with:

```text
pong
```

## The One File Most People Should Edit

The normal workflow for operators is:

- edit `.env`
- do not edit `compose.yaml`
- do not edit the Nginx template unless you need a non-standard proxy setup

The Nginx template is rendered from `.env` automatically when the Nginx container starts. It only substitutes the custom deployment values and leaves normal Nginx variables like `$host` untouched.

## Setting Reference

### Required for a basic deployment

- `VALOUR_IMAGE`: which Valour image to run
- `HTTP_PORT`: host port for HTTP redirects
- `HTTPS_PORT`: host port for HTTPS traffic
- `SERVER_NAMES`: hostnames Nginx should serve
- `PUBLIC_API_BASE_URL`: public HTTPS base URL of the deployment
- `TLS_CERT_PATH`: path to the certificate file
- `TLS_KEY_PATH`: path to the private key file
- `POSTGRES_DB`: database name
- `POSTGRES_USER`: database username
- `POSTGRES_PASSWORD`: database password
- `NODE_NAME`: node display name
- `NODE_KEY`: node secret

Recommended value pattern:

- use one hostname for everything
- set `SERVER_NAMES=chat.example.com`
- set `PUBLIC_API_BASE_URL=https://chat.example.com`

### Usually leave these alone

- `POSTGRES_IMAGE`
- `REDIS_IMAGE`
- `NGINX_IMAGE`
- `ASPNETCORE_ENVIRONMENT`
- `UPSTREAM_HOST`
- `UPSTREAM_PORT`
- `CLIENT_MAX_BODY_SIZE`
- `PROXY_READ_TIMEOUT`

## Certificates

### Which certificate option should I choose?

Choose a normal public certificate when:

- users may reach your hostname directly
- you are not using Cloudflare proxying
- you want the simplest browser-trusted setup

Choose Cloudflare Origin CA when:

- your hostname is proxied through Cloudflare
- Cloudflare is your public edge
- you want Cloudflare to validate the origin with `Full (strict)`

### Public certificate checklist

Make sure:

- the certificate matches the hostname in `SERVER_NAMES`
- the private key matches the certificate
- `TLS_CERT_PATH` points to the certificate file
- `TLS_KEY_PATH` points to the private key file

### Cloudflare certificate checklist

Make sure:

- the DNS record is proxied through Cloudflare
- the origin certificate hostname matches `SERVER_NAMES`
- the origin certificate and key are mounted where Nginx can read them
- Cloudflare SSL/TLS mode is set to `Full (strict)`

### Certificate renewals

If you use a public certificate such as Let's Encrypt, renew it on its normal schedule and keep the files at the same paths if possible.

If you use Cloudflare Origin CA, choose a validity period that matches how you want to manage renewals. After replacing the files, restart Nginx:

```bash
docker compose restart nginx
```

## Optional Features

Valour also supports additional features you can enable when you are ready.

### Uploads and media storage

Uploads require S3-compatible object storage. Add the following values to `.env` if you want uploads to work:

- `CDN__S3Access`
- `CDN__S3Secret`
- `CDN__S3Endpoint`
- `CDN__PublicS3Access`
- `CDN__PublicS3Secret`
- `CDN__PublicS3Endpoint`

Add these values when you want uploads and media storage enabled.

### Email

If you want real outgoing email, add:

- `Email__ApiKey`

### Cloudflare-backed features

If you want Cloudflare cache purge and related integrations, add:

- `Cloudflare__ApiKey`
- `Cloudflare__ZoneId`

If you want Cloudflare RealtimeKit voice, also add:

- `Cloudflare__RealtimeAccountId`
- `Cloudflare__RealtimeAppId`
- `Cloudflare__RealtimeApiToken`

### Push notifications

If you want Firebase push notifications, you will need:

- a Firebase credentials file mounted into the app container
- `Notifications__FirebaseCredentialPath` in `.env`

Mount that file into the app container using the path that fits your environment.

## Common Tasks

### Start the stack

```bash
docker compose up -d
```

### Stop the stack

```bash
docker compose down
```

### Restart the stack

```bash
docker compose restart
```

### Update to a newer image

1. Edit `VALOUR_IMAGE` in `.env` if you want to change tags.
2. Pull the new image and recreate containers:

```bash
docker compose pull
docker compose up -d
```

### View logs

```bash
docker compose logs -f dotnetapp
docker compose logs -f nginx
```

### Check running containers

```bash
docker compose ps
```

## Backups

For a basic deployment, the data you should protect is:

- the PostgreSQL volume
- the Redis volume
- your `.env` file
- your TLS certificate and key files

If you enable uploads with external object storage, you should also back up that storage according to your provider's backup plan.

At minimum, make sure you have a backup strategy for PostgreSQL. Redis is useful operational state, but PostgreSQL is the critical source of truth.

## Troubleshooting

### `https://your-domain/api/ping` is unavailable

Check:

- your DNS record points to the correct server
- ports `80` and `443` are open in your firewall
- the Nginx container is running
- your certificate paths are correct

Useful commands:

```bash
docker compose ps
docker compose logs --tail=100 nginx
```

### The app container keeps restarting

Check the app logs:

```bash
docker compose logs --tail=200 dotnetapp
```

Common causes:

- invalid database password
- PostgreSQL not healthy yet
- invalid environment variable names or values
- missing optional credentials for a feature you turned on

### PostgreSQL is not starting

Check:

- `POSTGRES_DB`
- `POSTGRES_USER`
- `POSTGRES_PASSWORD`

Then inspect logs:

```bash
docker compose logs --tail=200 postgres
```

### WebSocket or live updates do not work

Check the Nginx logs and confirm the stack is using the included proxy config:

```bash
docker compose logs --tail=200 nginx
```

The included Nginx template already proxies `/hubs/core` correctly. If you replace the proxy layer, make sure WebSocket upgrade headers are preserved.

### The site loads but some links or media still point to official `valour.gg` domains

The current stack can host the frontend, but some app and shared model paths still contain hardcoded official-domain assumptions. That means the main app can be served locally while a few generated URLs may still reference official Valour domains until those code paths are made configurable.

## Security Notes

- Change `POSTGRES_PASSWORD` before exposing the stack to the internet.
- Change `NODE_KEY` before exposing the stack to the internet.
- Keep `.env` private.
- Keep certificate key files private.
- Do not publish PostgreSQL or Redis ports directly to the public internet unless you know exactly why you are doing that.

## Deployment Notes

- This stack serves the frontend, API, and SignalR together from one hostname.
- PostgreSQL and Redis are included as part of the standard deployment.
