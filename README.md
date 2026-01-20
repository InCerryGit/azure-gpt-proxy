# AzureGptProxy ([中文](./README.zh-CN.md))

> This project proxies Anthropic Messages-style requests to Azure OpenAI, and converts responses back to an Anthropic-compatible format.
> It also exposes Cursor/OpenAI-compatible endpoints under the `/cursor` path.

## Features

- **Protocol adaptation**: Anthropic Messages -> Azure OpenAI Chat Completions / Responses
- **Response conversion**: Azure OpenAI -> Anthropic Messages format
- **SSE streaming**: `message_start`, `content_block_start`, `content_block_delta`, `content_block_stop`, `message_delta`, `message_stop`
- **Tool calls**: `tool_use` / `tool_result`
- **Token counting**: `POST /v1/messages/count_tokens` local estimation
- **Cursor proxy**: OpenAI-compatible `chat/completions` streaming via `/cursor/v1/chat/completions`

## Endpoints

- Anthropic-compatible:
  - `POST /v1/messages`
  - `POST /v1/messages/count_tokens`
- Cursor/OpenAI-compatible:
  - `GET /cursor/health`
  - `GET /cursor/v1/models` (also `/cursor/models`)
  - `POST /cursor/v1/chat/completions` (also `/cursor/chat/completions`)

## Cursor configuration

Cursor will call OpenAI-style paths like `/v1/models` and `/v1/chat/completions`. This proxy exposes those routes under the `/cursor` prefix, so your Base URL MUST include `/cursor`.

1. Expose this service to the public internet (Cursor requires a publicly reachable HTTPS URL).
2. In Cursor Settings > Models > API Keys:
   - Set **OpenAI Base URL** to `https://<your-public-host>/cursor` (example: `https://xxxx.trycloudflare.com/cursor`).
     - Do NOT add `/v1` here; Cursor will append `/v1/...` automatically.
   - Set **OpenAI API Key** to the same value as `ANTHROPIC_AUTH_TOKEN` (or leave it empty if auth is disabled).
3. Create custom models: `gpt-high`, `gpt-medium`, `gpt-low` (optional: `gpt-minimal`).
   - These model ids are used to choose `reasoning.effort` for Azure Responses.

Notes:
- Cursor endpoints require `CURSOR_AZURE_DEPLOYMENT` (Azure deployment name, not model name).
- If you enable auth (`ANTHROPIC_AUTH_TOKEN`), requests must include `Authorization: Bearer <token>`.

## Expose to the public internet (Cloudflare Tunnel)

Cursor MUST be able to reach your API from the public internet (HTTP/HTTPS). If your proxy only listens on `localhost` or a private LAN IP, Cursor will not work.

### Option A: Quick temporary URL (trycloudflare.com)

1. Download and install `cloudflared`: https://developers.cloudflare.com/cloudflare-one/connections/connect-apps/install-and-setup/installation/
2. Run a tunnel that forwards to your local service (example local port `8080`):

```powershell
cloudflared tunnel --url http://localhost:8080
```

`cloudflared` will print a public URL like `https://xxxx.trycloudflare.com`.

Then set Cursor **OpenAI Base URL** to:
- `https://xxxx.trycloudflare.com/cursor`

### Option B: Stable URL with your own domain

High-level steps:

1. `cloudflared tunnel login`
2. `cloudflared tunnel create <name>`
3. Add a DNS route to your domain:

```powershell
cloudflared tunnel route dns <name> ai-proxy.your-domain.com
```

4. Create `config.yml` (example):

```yml
tunnel: <name-or-uuid>
credentials-file: C:\\Users\\<you>\\.cloudflared\\<uuid>.json

ingress:
  - hostname: ai-proxy.your-domain.com
    service: http://localhost:8080
  - service: http_status:404
```

5. Run the tunnel:

```powershell
cloudflared tunnel --config .\config.yml run
```

Then set Cursor **OpenAI Base URL** to:
- `https://ai-proxy.your-domain.com/cursor`

## Run locally (Windows)

### 1) Prepare environment variables

Copy `.env.sample` to `.env` and fill in values:

```bat
copy .env.sample .env
```

### 2) Start the service

```powershell
./start.ps1
```

The listening address is controlled by `ASPNETCORE_URLS`. Startup logs will print the final URL(s).

### 3) Quick verification

- `GET http://localhost:8080/cursor/health`

## Docker

### Build image

```bash
docker build -t azuregptproxy:latest .
```

### Run container

```bash
docker rm -f azuregptproxy

docker run -d --name azuregptproxy --env-file .env -p 8088:8080 azuregptproxy:latest
```

## Environment variables

| Name | Required | Description |
|------|----------|-------------|
| `AZURE_OPENAI_ENDPOINT` | yes | Azure OpenAI resource endpoint. Examples: `https://<resource>.openai.azure.com/` (api-version can also be embedded as `?api-version=...`). |
| `AZURE_OPENAI_API_KEY` | yes | Azure OpenAI API key. |
| `AZURE_API_VERSION` | recommended | API version (e.g. `2024-10-21`). If omitted, the proxy will try to reuse `api-version` from `AZURE_OPENAI_ENDPOINT` if present. |
| `ANTHROPIC_AUTH_TOKEN` | no | If set, `/v1/messages*` and `/cursor/*` (except `/cursor/health`) require `Authorization: Bearer <token>`. |
| `CURSOR_AZURE_DEPLOYMENT` | required for Cursor | Azure deployment name used for Cursor endpoints (Azure Responses API `model` field). |
| `SMALL_MODEL` | recommended | Azure deployment name used when model looks like `*haiku*`. |
| `BIG_MODEL` | recommended | Azure deployment name used when model looks like `*sonnet*` or `*opus*`. |
| `SMALL_EFFORT` | no | Reasoning effort for `SMALL_MODEL` when `thinking` is enabled (minimal\|low\|medium\|high; default: medium). |
| `BIG_EFFORT` | no | Reasoning effort for `BIG_MODEL` when `thinking` is enabled (minimal\|low\|medium\|high; default: medium). |

## License

MIT
