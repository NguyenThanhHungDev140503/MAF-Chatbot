---
last_mapped_commit: 77ef075b9c1be6df3639fa35df7014a9e493fe18
---
<!-- refreshed: 2026-10-23 -->
# Architecture

**Analysis Date:** 2026-10-23

## System Overview

```text
Console user
    |
CLI composition root and console loop
code/src/ErpChatbot.Cli/Program.cs
    | command classification: code/src/ErpChatbot.Cli/CliRunner.cs
    | scoped IErpAgentService
ErpAgentService -> AIAgent -> AgentSession
code/src/ErpChatbot.Agent/Services/ErpAgentService.cs
    | injected IChatClient
ChatClientFactory -> ChatClientBuilder -> OllamaApiClient -> Ollama
code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs

Shared contracts/options/state models: code/src/ErpChatbot.Core/
```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| CLI startup | Configuration, DI, health, terminal loop | `code/src/ErpChatbot.Cli/Program.cs` |
| CliCommandHandler | Input classification and local commands | `code/src/ErpChatbot.Cli/CliRunner.cs` |
| ErpAgentService | Agent/session lifecycle, text streaming, usage | `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs` |
| ChatClientFactory | Ollama construction/defaults and health request | `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs` |
| Core | Host-independent contracts and shared models | `code/src/ErpChatbot.Core/Interfaces/`, `code/src/ErpChatbot.Core/Models/` |

## Pattern Overview

**Overall:** Layered .NET console application with dependency inversion and a CLI composition root.

**Key Characteristics:**
- `code/ErpChatbot.sln` contains Core, Infrastructure, Agent, CLI and Tests; Agent and CLI implementations exist as tracked source.
- Project references form CLI -> Agent -> Core and CLI -> Infrastructure -> Core; Agent has no Infrastructure reference (`code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj`, `code/src/ErpChatbot.Agent/ErpChatbot.Agent.csproj`, `code/src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj`).
- Core is provider-independent but exposes `IChatClient` through an explicit `Microsoft.Extensions.AI.Abstractions` dependency (`code/src/ErpChatbot.Core/ErpChatbot.Core.csproj`).
- The product has no ERP tool integration, database, HTTP server or authentication. An independent ASP.NET Core ERP assistant is project intent in `.planning/PROJECT.md`, not the current executable.

## Layers

**CLI / composition:**
- Purpose: Construct dependencies and own interactive execution.
- Location: `code/src/ErpChatbot.Cli/`.
- Contains: Startup/configuration, service wiring, prompt loop, local commands, timing, cancellation and terminal rendering.
- Depends on: Core, Agent, Infrastructure and hosting/configuration/DI.
- Used by: Console users through `code/src/ErpChatbot.Cli/Program.cs`.

**Agent orchestration:**
- Purpose: Adapt the injected client into a stateful MAF agent.
- Location: `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Contains: `AIAgent`, lazy `AgentSession`, streaming and completed-turn usage accumulation.
- Depends on: Core, `IChatClient`, MAF and options; no provider construction.
- Used by: CLI through `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`.

**Provider infrastructure:**
- Purpose: Encapsulate external model connectivity.
- Location: `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.
- Contains: Ollama-only provider selection, generation defaults and real health request with a default five-second deadline.
- Depends on: Core, Microsoft.Extensions.AI, OllamaSharp and options.
- Used by: CLI startup and DI in `code/src/ErpChatbot.Cli/Program.cs`.

**Core contracts/models:**
- Purpose: Share application interfaces, configuration and statistics types.
- Location: `code/src/ErpChatbot.Core/`.
- Contains: `IErpAgentService`, `IChatClientFactory`, `LlmOptions`, `ErpOptions`, `SessionStats`, `ChatTurnResponse`.
- Depends on: .NET and AI abstractions; no application-project reference.
- Used by: All application layers and `code/tests/ErpChatbot.Tests/`.

## Data Flow

### Primary Request Path

1. `code/src/ErpChatbot.Cli/Program.cs` creates a Generic Host builder, loads required `appsettings.json` relative to the executable, adds environment configuration, binds options and applies explicit `LLM_PROVIDER`, `LLM_MODEL`, `OLLAMA_ENDPOINT` overrides.
2. It registers singleton factory/client and scoped agent/command handler, builds the host and creates one scope for the CLI conversation. Agent/client resolution occurs before health checking (`code/src/ErpChatbot.Cli/Program.cs`).
3. Health creates a separate disposable client, sends `ping` with a one-token output limit and reports readiness or diagnostic text. Unhealthy status does not stop the console loop (`code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`, `code/src/ErpChatbot.Cli/Program.cs`).
4. `code/src/ErpChatbot.Cli/CliRunner.cs` trims input for command classification. Blank input is ignored and slash commands run locally; ordinary input returns `SendToLlm`. CLI passes the original prompt string onward.
5. `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs` lazily creates a session, invokes `AIAgent.RunStreamingAsync` using that session and yields nonempty update text.
6. CLI writes chunks immediately. Agent sums `UsageContent.Details` input/output counts and calls `SessionStats.RecordTurn` only after normal stream completion (`code/src/ErpChatbot.Cli/Program.cs`, `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`).
7. CLI prints elapsed time and per-turn usage calculated from the difference between captured and updated cumulative totals (`code/src/ErpChatbot.Cli/Program.cs`).

### Local Commands and Cancellation

1. `/reset` replaces the session; `/stats` reads cumulative statistics; `/info` and `/help` print configuration; `/exit` and `/quit` terminate. Unknown slash commands print guidance (`code/src/ErpChatbot.Cli/CliRunner.cs`).
2. Reset preserves cumulative statistics (`code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`).
3. After each input line, CLI installs a `Console.CancelKeyPress` handler tied to that turn's token source and removes it in `finally` (`code/src/ErpChatbot.Cli/Program.cs`).
4. Cancellation/stream failure propagates to CLI without replacing the service's session. Incomplete turns do not reach `RecordTurn` (`code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`).

**State Management:**
- One service instance owns one mutable `AgentSession` and `SessionStats`; the CLI scope spans the whole interactive conversation (`code/src/ErpChatbot.Cli/Program.cs`, `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`).
- `GetStats()` returns the live statistics object. Capture numeric totals before a turn for deltas (`code/src/ErpChatbot.Core/Models/SessionStats.cs`, `code/src/ErpChatbot.Cli/Program.cs`).
- No persistent history or explicit context-size policy is implemented in `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.

## Key Abstractions

**IErpAgentService:**
- Purpose: Streaming text, session reset and statistics without provider details.
- Examples: `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`, `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Pattern: Scoped stateful application service; one instance per conversation.

**IChatClientFactory / IChatClient:**
- Purpose: Separate provider construction/health from agent orchestration.
- Examples: `code/src/ErpChatbot.Core/Interfaces/IChatClientFactory.cs`, `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.
- Pattern: Factory interface plus injected abstraction. Fake chat clients are local test doubles in `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`.

**Options and models:**
- Purpose: Centralize provider settings, assistant instructions and cumulative usage.
- Examples: `code/src/ErpChatbot.Core/Models/LlmOptions.cs`, `code/src/ErpChatbot.Core/Models/ErpOptions.cs`, `code/src/ErpChatbot.Core/Models/SessionStats.cs`.
- Pattern: Mutable options bound through `IOptions<T>`; the separate record in `code/src/ErpChatbot.Core/Models/ChatTurnResponse.cs` is not used in the streaming execution path.

## Entry Points

**Application:**
- Location: `code/src/ErpChatbot.Cli/Program.cs`.
- Triggers: Run `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj`.
- Responsibilities: Configuration, DI, health, terminal loop and error presentation. The Generic Host is built for services/configuration; no web listener or hosted-service run loop is started.

**Tutorial executables:**
- Location: `tutorial/ngay-01/Ex1/Program.cs` through `tutorial/ngay-01/Ex4/Program.cs`; `tutorial/ngay-02/Ex1/Program.cs` through `tutorial/ngay-02/Ex4/Program.cs`.
- Triggers: Run each independent tutorial project, outside `code/ErpChatbot.sln`.
- Responsibilities: Day 1 illustrates chat/streaming/session basics; Day 2 illustrates manual/automatic function calling, sample inventory/order tools and write approval. The purchase function in `tutorial/ngay-02/Ex4/Program.cs` generates a sample result without ERP persistence.

## Architectural Constraints

- **Conversation isolation:** Keep instances isolated per conversation. The interface explicitly forbids a singleton shared between web users (`code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`).
- **Threading:** CLI serializes turns; session/statistics have no locks or concurrent-turn coordination (`code/src/ErpChatbot.Cli/Program.cs`, `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`).
- **Global state:** Factory/client are host singletons; terminal state/events are process-wide (`code/src/ErpChatbot.Cli/Program.cs`).
- **Circular imports:** No circular application-project reference is detected in `code/src/ErpChatbot.Core/ErpChatbot.Core.csproj`, `code/src/ErpChatbot.Agent/ErpChatbot.Agent.csproj`, `code/src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj`, `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj`.
- **Scope:** ERP tools, identity enforcement and durable sessions are future requirements in `.planning/PROJECT.md`; tutorial tools do not establish product capabilities.

## Anti-Patterns

### Treating Core as Free of AI Dependencies

**What happens:** `code/README.md` describes Core as having no third-party AI dependency.
**Why it's wrong:** Its project manifest includes AI abstractions to express `IChatClient` (`code/src/ErpChatbot.Core/ErpChatbot.Core.csproj`).
**Do this instead:** Preserve and accurately describe the provider-independent seam in `code/src/ErpChatbot.Core/Interfaces/IChatClientFactory.cs`.

### Startup Validation Outside the Interactive Error Boundary

**What happens:** Agent/client resolution precedes health diagnostics and the turn-level try/catch (`code/src/ErpChatbot.Cli/Program.cs`).
**Why it's wrong:** Unsupported providers or invalid endpoint construction can terminate startup before interactive diagnostics (`code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`).
**Do this instead:** When extending startup, validate options and present construction failures at the CLI composition boundary while retaining explicit provider rejection (`code/src/ErpChatbot.Cli/Program.cs`).

## Error Handling

**Strategy:** Health failures become `(IsHealthy, Message)`; inference failures propagate to the CLI.

**Patterns:**
- Health catches its deadline timeout, model-missing message patterns, HTTP errors and remaining exceptions (`code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`).
- Agent rethrows stream/cancellation failures and keeps the session field (`code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`).
- CLI catches cancellation, HTTP failures and general turn exceptions, then accepts another prompt (`code/src/ErpChatbot.Cli/Program.cs`).

## Cross-Cutting Concerns

**Logging:** Application output/diagnostics use `Console`; application `ILogger` usage and telemetry wiring are not detected in `code/src/ErpChatbot.Cli/Program.cs` or `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
**Validation:** Blank prompts/commands are handled explicitly; provider selection rejects non-Ollama values and URI construction checks endpoint syntax. No options-validation pipeline is registered (`code/src/ErpChatbot.Cli/CliRunner.cs`, `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`, `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`, `code/src/ErpChatbot.Cli/Program.cs`).
**Authentication:** Not implemented in application projects. Instructions acknowledge unavailable ERP data (`code/src/ErpChatbot.Core/Models/ErpOptions.cs`); user-specific authorization is project intent in `.planning/PROJECT.md`.
**Verification boundary:** Source/references are inspected; no build, automated test or live Ollama request is executed for this map. Test source in `code/tests/ErpChatbot.Tests/` demonstrates intended behavior without establishing a passing run.

---

*Architecture analysis: 2026-10-23*
