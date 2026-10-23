---
last_mapped_commit: 77ef075b9c1be6df3639fa35df7014a9e493fe18
---

# Technology Stack

**Analysis Date:** 2026-10-23

## Languages

**Primary:**
- C# across `code/src/` and `code/tests/ErpChatbot.Tests/`; no explicit `LangVersion` is declared in the project files, so the language version is not independently pinned.

**Secondary:**
- JSON configuration in `code/src/ErpChatbot.Cli/appsettings.json`, XML/MSBuild manifests such as `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj`.
- Tutorial C# programs under `tutorial/ngay-01/` and `tutorial/ngay-02/` are separate from the application solution `code/ErpChatbot.sln`.

## Runtime

**Environment:**
- .NET 10: all five projects in `code/ErpChatbot.sln` target `net10.0`; `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj` is the executable.
- Live chat needs an Ollama server and downloaded model through `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`; the daemon version is not pinned.
- No `global.json` is detected at the repository root or under `code/`; SDK patch version is not selected independently of the target framework.

**Package Manager:**
- NuGet through SDK/MSBuild `PackageReference`, direct versions in `code/src/*/*.csproj` and `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`.
- Lockfile: no `packages.lock.json` detected in `code/` or `tutorial/`; generated `obj/` restore output is excluded from the map.

## Frameworks

**Core:**
- Microsoft Agent Framework, `Microsoft.Agents.AI` 1.24.0: wraps `IChatClient` as `AIAgent` and manages `AgentSession` in `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Microsoft.Extensions.AI 10.10.0: client composition/usage content; references in `code/src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj` and `code/src/ErpChatbot.Agent/ErpChatbot.Agent.csproj`.
- Microsoft.Extensions.Hosting 10.0.0: console host/configuration/DI in `code/src/ErpChatbot.Cli/Program.cs`; no implemented ASP.NET Core endpoint.

**Testing:**
- xUnit 2.9.3, xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1 in `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`.
- FluentAssertions 8.0.1 and coverlet.collector 6.0.4 in `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj` for assertions and coverage collection.

**Build/Dev:**
- SDK-style `Microsoft.NET.Sdk` projects and MSBuild solution `code/ErpChatbot.sln`; application projects enable nullable reference types and implicit usings.
- `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj` copies `appsettings.json` to output with `PreserveNewest`.
- No CI workflow, container definition or centralized `Directory.Build.*`/`Directory.Packages.props` configuration is detected for `code/ErpChatbot.sln`.

## Key Dependencies

**Critical:**
- Microsoft.Extensions.AI.Abstractions 10.10.0 in `code/src/ErpChatbot.Core/ErpChatbot.Core.csproj`: `code/src/ErpChatbot.Core/Interfaces/IChatClientFactory.cs` returns `IChatClient`. Core is provider-independent, but has an AI abstraction dependency.
- Microsoft.Agents.AI 1.24.0 in `code/src/ErpChatbot.Agent/ErpChatbot.Agent.csproj`: session-based streaming agent.
- OllamaSharp 5.5.0 in `code/src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj`: sole implemented application provider.

**Infrastructure:**
- Microsoft.Extensions.Options 10.0.0 in `code/src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj` and `code/src/ErpChatbot.Agent/ErpChatbot.Agent.csproj`: injects LLM and ERP options.
- Microsoft.Extensions.Hosting 10.0.0 in `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj`: console composition root.

**Tutorial-only versions:**
- `tutorial/ngay-01/Ex1/Ex1.csproj` through `tutorial/ngay-01/Ex4/Ex4.csproj` reference OpenAI 2.14.0, Microsoft.Extensions.AI.OpenAI 10.10.1 and Microsoft.Extensions.AI 10.10.0; Ex4 adds Microsoft.Agents.AI 1.24.0.
- `tutorial/ngay-02/Ex1/Ex1.csproj` through `tutorial/ngay-02/Ex4/Ex4.csproj` use Microsoft.Extensions.AI/Microsoft.Extensions.AI.OpenAI 10.11.0, OpenAI 2.14.0 and OllamaSharp 5.5.0; Ex4 uses Microsoft.Agents.AI 1.25.0. Do not treat tutorial versions/provider branches as the application baseline.

## Configuration

**Environment:**
- `code/src/ErpChatbot.Cli/Program.cs` creates the host, loads required `appsettings.json` from `AppContext.BaseDirectory`, adds environment variables and binds `Llm`/`Erp` sections.
- Nonblank `LLM_PROVIDER`, `LLM_MODEL`, `OLLAMA_ENDPOINT` explicitly override bound LLM options through `PostConfigure` in `code/src/ErpChatbot.Cli/Program.cs`.
- `code/src/ErpChatbot.Cli/appsettings.json` and `code/src/ErpChatbot.Core/Models/LlmOptions.cs` default to Ollama, model `llama3.2`, local port 11434, temperature 0.2, output limit 500 tokens.
- `code/src/ErpChatbot.Core/Models/ErpOptions.cs` holds labels/instructions, not an ERP connection.
- `AGENTS.md` requires Context7 verification for library API work; these are source-declared versions, not claims of latest package availability.

**Build:**
- Use `code/ErpChatbot.sln` for application projects; tutorials under `tutorial/ngay-01/` and `tutorial/ngay-02/` are separate executables.
- Commands in `code/README.md` can be addressed from repository root as `dotnet run --project code/src/ErpChatbot.Cli` and `dotnet test code/ErpChatbot.sln`.

## Platform Requirements

**Development:**
- .NET SDK supporting `net10.0` and NuGet restore access for `code/ErpChatbot.sln`; no OS-specific application target is declared.
- Live interaction needs Ollama serving the configured model in `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.
- Test fakes in `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs` and `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs` avoid live LLM dependencies.

**Production:**
- Console artifact `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj`; no production deployment provider/pipeline is configured.
- ASP.NET Core, ERP APIs, MCP and durable history are future scope in `.planning/PROJECT.md`, not implemented runtime dependencies in `code/src/`.
- No restore/build/test/live inference was run for this survey; manifests and test source do not establish successful execution.

---

*Stack analysis: 2026-10-23*
