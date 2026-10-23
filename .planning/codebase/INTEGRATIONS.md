---
last_mapped_commit: 77ef075b9c1be6df3639fa35df7014a9e493fe18
---

# External Integrations

**Analysis Date:** 2026-10-23

## APIs & External Services

**Local LLM — implemented application integration:**
- Ollama serves chat generation in `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.
  - SDK/Client: OllamaSharp 5.5.0 `OllamaApiClient`, composed through Microsoft.Extensions.AI `ChatClientBuilder`; references in `code/src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj`.
  - Connection: `Llm:Ollama:Endpoint` plus explicit `OLLAMA_ENDPOINT` override in `code/src/ErpChatbot.Cli/Program.cs`; local default configured in `code/src/ErpChatbot.Cli/appsettings.json`.
  - Model: `Llm:Model` or `LLM_MODEL`, default `llama3.2` in `code/src/ErpChatbot.Core/Models/LlmOptions.cs`.
  - Auth: no API key/header configured by `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.
  - Operations: streaming requests originate in `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`; SDK HTTP paths are not hardcoded in application source.
  - Health: `CheckHealthAsync` in `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs` sends real `ping` completion with a one-token cap, default five-second cancellation and handling for timeout, missing model and connection errors.
  - Rate limits/retries: no explicit policy in `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.
  - Provider: application factory rejects unsupported providers in `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.

**Tutorial-only client branches:**
- `tutorial/ngay-01/Ex3/Program.cs` and `tutorial/ngay-02/Ex1/Program.cs` through `tutorial/ngay-02/Ex4/Program.cs` support OpenAI-compatible clients alongside Ollama.
  - SDK: OpenAI 2.14.0 and Microsoft.Extensions.AI.OpenAI, versions declared in the corresponding tutorial `.csproj` files.
  - Configuration names: `LLM_PROVIDER`, `LLM_MODEL`, `OLLAMA_ENDPOINT`, `OPENAI_ENDPOINT`, `OPENAI_API_KEY` in `tutorial/ngay-02/Ex1/Program.cs`; credential values are not recorded.
  - These executables are absent from `code/ErpChatbot.sln`; they do not enable another provider in the application.

**ERP/MCP — future scope:**
- `code/src/ErpChatbot.Core/Models/ErpOptions.cs` explicitly states ERP data is unavailable; `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs` registers no business tools.
- `tutorial/ngay-02/Ex3/Program.cs` uses in-memory stock/order dictionaries. `tutorial/ngay-02/Ex4/Program.cs` simulates purchase requests with generated IDs; neither calls or writes a real ERP.
- ERP adapters, MCP, web UI and additional providers are future scope in `.planning/PROJECT.md`.

## Data Storage

**Databases:**
- No database client, migrations or connection-string binding detected in `code/src/` and `code/src/*/*.csproj`.
- `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs` stores an in-memory `AgentSession` and `SessionStats`; neither is durable storage.

**File Storage:**
- Local JSON configuration input in `code/src/ErpChatbot.Cli/Program.cs`, copied by `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj`; no object storage, upload or transcript export integration detected.

**Caching:**
- No external cache in `code/src/*/*.csproj`; session fields in `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs` are process-local state.

## Authentication & Identity

**Auth Provider:**
- None implemented in `code/src/ErpChatbot.Cli/Program.cs`: no application sign-in, ERP identity exchange or authorization middleware.
- One scoped agent service in `code/src/ErpChatbot.Cli/Program.cs` is a CLI lifetime; it is not web session ownership enforcement.

**OAuth Integrations:**
- Not detected in `code/src/*/*.csproj`; user-scoped ERP access is future scope in `.planning/PROJECT.md`.

## Monitoring & Observability

**Error Tracking:**
- No external tracker detected in `code/src/*/*.csproj`; console error handling covers cancellation, HTTP failures and exceptions in `code/src/ErpChatbot.Cli/Program.cs`.

**Analytics:**
- No external service in `code/src/*/*.csproj`; local token/turn stats are held by `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs` and `code/src/ErpChatbot.Core/Models/SessionStats.cs`.

**Logs:**
- `Console` output in `code/src/ErpChatbot.Cli/Program.cs` and `code/src/ErpChatbot.Cli/CliRunner.cs`; no application log shipping or custom structured logger integration detected.
- OpenTelemetry remains future scope in `.planning/PROJECT.md`; no telemetry backend is configured in `code/src/`.

## CI/CD & Deployment

**Hosting:**
- Local console entry point `code/src/ErpChatbot.Cli/Program.cs`; no deployment provider/container definition detected for `code/ErpChatbot.sln`.
- NuGet package restore is an external build dependency from `code/src/*/*.csproj` and `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`; no custom feed configuration detected.

**CI Pipeline:**
- Not detected: no `.github/workflows/` directory; `code/README.md` documents manual run/test commands.

## Environment Configuration

**Development:**
- `LLM_PROVIDER`, `LLM_MODEL`, `OLLAMA_ENDPOINT` are optional explicit overrides: `code/src/ErpChatbot.Cli/appsettings.json` provides defaults and `code/src/ErpChatbot.Cli/Program.cs` applies only nonblank overrides.
- Standard environment configuration is added before `Llm`/`Erp` binding in `code/src/ErpChatbot.Cli/Program.cs`; no dotenv loader is implemented.
- Secrets location: no application credential store is configured in `code/src/ErpChatbot.Cli/Program.cs`; the implemented local connection requires no credential setting.
- Test doubles: fake clients in `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs` and `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`, fake agent in `code/tests/ErpChatbot.Tests/Cli/CliCommandTests.cs`.

**Staging:**
- No staging-specific settings/hosting detected alongside `code/src/ErpChatbot.Cli/appsettings.json`.

**Production:**
- No production-specific secret management/failover/environment setup detected in `code/src/`; `.planning/PROJECT.md` retains web/ERP rollout as future scope.
- This map does not verify live Ollama availability or execute build/tests.

## Webhooks & Callbacks

**Incoming:**
- None: `code/src/ErpChatbot.Cli/Program.cs` reads console input and exposes no HTTP routes.

**Outgoing:**
- No webhook delivery detected in `code/src/`; outgoing LLM requests use `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs` and `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- `Console.CancelKeyPress` in `code/src/ErpChatbot.Cli/Program.cs` cancels a local turn; it is not an external callback endpoint.

---

*Integration audit: 2026-10-23*
