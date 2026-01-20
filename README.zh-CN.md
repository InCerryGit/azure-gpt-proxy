# AzureGptProxy ([English](./README.md))

> 该项目用于将 Anthropic Messages API 风格的请求代理到 Azure OpenAI，并在响应侧转换回 Anthropic 兼容格式。
> 同时提供 Cursor/OpenAI 兼容的代理入口（统一挂在 `/cursor` 路径下）。

## 功能简介

- **协议适配**：Anthropic Messages -> Azure OpenAI Chat Completions / Responses
- **响应转换**：Azure OpenAI -> Anthropic Messages 格式
- **SSE 流式支持**：`message_start` / `content_block_start` / `content_block_delta` / `content_block_stop` / `message_delta` / `message_stop`
- **Tool 调用支持**：`tool_use` / `tool_result`
- **Token 统计支持**：`POST /v1/messages/count_tokens` 本地估算
- **Cursor 代理**：通过 `/cursor/v1/chat/completions` 提供 OpenAI 风格的流式接口

## 接口一览

- Anthropic 兼容接口：
  - `POST /v1/messages`
  - `POST /v1/messages/count_tokens`
- Cursor/OpenAI 兼容接口：
  - `GET /cursor/health`
  - `GET /cursor/v1/models`（也支持 `/cursor/models`）
  - `POST /cursor/v1/chat/completions`（也支持 `/cursor/chat/completions`）

## Cursor 配置（重要）

Cursor 会以 OpenAI 的方式拼接路径（例如自动请求 `/v1/models`、`/v1/chat/completions`）。
本项目把这些 OpenAI 风格的路由放在 `/cursor` 前缀下，因此你在 Cursor 里配置的 Base URL 必须带上 `/cursor`。

1. 将服务暴露到公网（Cursor 通常需要公网可访问的 HTTPS URL）。
2. 打开 Cursor Settings > Models > API Keys：
   - **OpenAI Base URL** 填：`https://<你的公网域名>/cursor`
     - 示例：`https://xxxx.trycloudflare.com/cursor`
     - 不要在这里手动加 `/v1`，Cursor 会自动拼上 `/v1/...`。
   - **OpenAI API Key**：填写 `ANTHROPIC_AUTH_TOKEN` 的值（若未启用鉴权可留空）。
3. 新建自定义模型：`gpt-high`、`gpt-medium`、`gpt-low`（可选：`gpt-minimal`）。
   - 这些 model id 用于映射 Azure Responses 的 `reasoning.effort`（high/medium/low/minimal）。

补充说明：
- Cursor 相关接口需要配置 `CURSOR_AZURE_DEPLOYMENT`（Azure 里的 Deployment name，不是模型名）。
- 若配置了 `ANTHROPIC_AUTH_TOKEN`，则 `/v1/messages*` 和 `/cursor/*`（除 `/cursor/health`）都必须带 `Authorization: Bearer <token>`。

## 通过 Cloudflare Tunnel 暴露到公网（Cursor 必须公网可访问）

Cursor 必须能从公网（HTTP/HTTPS）访问到你的 API。也就是说：服务只监听在 `localhost` 或者仅内网可访问时，Cursor 是无法使用的。

### 方案 A：快速临时公网地址（trycloudflare.com）

1. 安装 `cloudflared`：https://developers.cloudflare.com/cloudflare-one/connections/connect-apps/install-and-setup/installation/
2. 启动隧道转发到本地服务（假设本地端口 `8080`）：

```powershell
cloudflared tunnel --url http://localhost:8080
```

`cloudflared` 会输出类似 `https://xxxx.trycloudflare.com` 的公网地址。

然后在 Cursor 的 **OpenAI Base URL** 填：
- `https://xxxx.trycloudflare.com/cursor`

### 方案 B：绑定你自己的域名（稳定地址）

大致步骤：

1. `cloudflared tunnel login`
2. `cloudflared tunnel create <name>`
3. 把隧道绑定到你的域名：

```powershell
cloudflared tunnel route dns <name> ai-proxy.your-domain.com
```

4. 创建 `config.yml`（示例）：

```yml
tunnel: <name-or-uuid>
credentials-file: C:\\Users\\<you>\\.cloudflared\\<uuid>.json

ingress:
  - hostname: ai-proxy.your-domain.com
    service: http://localhost:8080
  - service: http_status:404
```

5. 运行隧道：

```powershell
cloudflared tunnel --config .\config.yml run
```

然后在 Cursor 的 **OpenAI Base URL** 填：
- `https://ai-proxy.your-domain.com/cursor`

## 本地运行（Windows）

### 1) 准备环境变量

复制 `.env.sample` 为 `.env` 并按需填写：

```bat
copy .env.sample .env
```

### 2) 启动服务

```powershell
./start.ps1
```

监听地址由 `ASPNETCORE_URLS` 控制，启动日志会输出最终 URL。

### 3) 快速验证

- `GET http://localhost:8080/cursor/health`

## Docker

### 构建镜像

```bash
docker build -t azuregptproxy:latest .
```

### 运行容器

```bash
docker rm -f azuregptproxy

docker run -d --name azuregptproxy --env-file .env -p 8088:8080 azuregptproxy:latest
```

## 环境变量

| 变量名 | 必填 | 说明 |
|--------|------|------|
| `AZURE_OPENAI_ENDPOINT` | 是 | Azure OpenAI 资源端点。示例：`https://<resource>.openai.azure.com/`（也支持把 `api-version` 放到 URL 里：`?api-version=...`）。 |
| `AZURE_OPENAI_API_KEY` | 是 | Azure OpenAI Key。 |
| `AZURE_API_VERSION` | 建议 | API 版本（如 `2024-10-21`）。如果不填，且 `AZURE_OPENAI_ENDPOINT` 自带 `?api-version=`，会优先复用。 |
| `ANTHROPIC_AUTH_TOKEN` | 否 | 若设置，则 `/v1/messages*` 与 `/cursor/*`（除 `/cursor/health`）需要 `Authorization: Bearer <token>`。 |
| `CURSOR_AZURE_DEPLOYMENT` | Cursor 必填 | Cursor 端点使用的 Azure 部署名（用于 Azure Responses API 的 `model` 字段）。 |
| `SMALL_MODEL` | 建议 | 当请求模型名包含 `haiku` 时映射到的 Azure 部署名。 |
| `BIG_MODEL` | 建议 | 当请求模型名包含 `sonnet`/`opus` 时映射到的 Azure 部署名。 |
| `SMALL_EFFORT` | 否 | `thinking` 启用时，`SMALL_MODEL` 使用的 reasoning effort（minimal\|low\|medium\|high；默认 medium）。 |
| `BIG_EFFORT` | 否 | `thinking` 启用时，`BIG_MODEL` 使用的 reasoning effort（minimal\|low\|medium\|high；默认 medium）。 |

## License

MIT
