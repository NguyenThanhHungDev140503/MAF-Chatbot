---
last_mapped_commit: 77ef075b9c1be6df3639fa35df7014a9e493fe18
---
# Codebase Structure

**Analysis Date:** 2026-10-23

## Directory Layout

```text
MAF-Chatbot/
├── AGENTS.md                     # Project instructions and source hierarchy
├── .gitignore                    # .NET build-output exclusions
├── .planning/                    # Active GSD scope and maps
│   ├── codebase/                 # Source-analysis documents
│   ├── phases/                   # Phase directories/placeholders
│   ├── reference/                # Source and milestone reconciliation
│   └── graphs/                   # Generated graph artifacts
├── code/                         # Product solution
│   ├── ErpChatbot.sln            # Four application projects and tests
│   ├── README.md                 # Product runbook
│   ├── src/                      # Core, Infrastructure, Agent, CLI
│   └── tests/                    # Layer-organized test assembly
├── docs/
│   └── superpowers/              # Day 1 design and implementation plan
├── tutorial/                     # Independent learning/reference material
│   ├── ngay-01/                  # Ex1–Ex4 chat and agent exercises
│   ├── ngay-02/                  # Ex1–Ex4 tool-calling exercises
│   ├── roadmap/                  # Whole-project roadmap source
│   └── ref/                      # Supplemental references
└── graphify-out/                 # Generated graph output
```

## Directory Purposes

**`code/src/ErpChatbot.Core/`:**
- Purpose: Shared interfaces and data/configuration models.
- Contains: `Interfaces/`, `Models/` and project manifest.
- Key files: `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`, `code/src/ErpChatbot.Core/Interfaces/IChatClientFactory.cs`, `code/src/ErpChatbot.Core/Models/LlmOptions.cs`, `code/src/ErpChatbot.Core/Models/ErpOptions.cs`, `code/src/ErpChatbot.Core/Models/SessionStats.cs`.
- `code/src/ErpChatbot.Core/ErpChatbot.Core.csproj` declares the AI abstraction dependency; no provider implementation belongs here.

**`code/src/ErpChatbot.Infrastructure/`:**
- Purpose: Provider construction/health behind Core contracts.
- Contains: `Factories/` and project manifest.
- Key file: `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.
- Provider dependencies belong in `code/src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj`.

**`code/src/ErpChatbot.Agent/`:**
- Purpose: Agent/session orchestration independent of provider infrastructure.
- Contains: `Services/` and project manifest.
- Key file: `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Preserve Core-only application-project references in `code/src/ErpChatbot.Agent/ErpChatbot.Agent.csproj`.

**`code/src/ErpChatbot.Cli/`:**
- Purpose: Executable host and terminal interface.
- Contains: Startup, command handling and settings.
- Key files: `code/src/ErpChatbot.Cli/Program.cs` wires dependencies; `code/src/ErpChatbot.Cli/CliRunner.cs` contains `CliCommandHandler` and `CliInputResult`.
- `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj` copies `code/src/ErpChatbot.Cli/appsettings.json` to the executable directory.

**`code/tests/ErpChatbot.Tests/`:**
- Purpose: One test assembly organized by application layer.
- Contains: `Core/`, `Infrastructure/`, `Agent/`, `Cli/`.
- Key files: `code/tests/ErpChatbot.Tests/Core/CoreModelsTests.cs`, `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`, `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`, `code/tests/ErpChatbot.Tests/Cli/CliCommandTests.cs`.
- `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj` references all four application projects.

**`.planning/`:**
- Purpose: Active project scope, requirements and execution state.
- Key files: `.planning/PROJECT.md`, `.planning/REQUIREMENTS.md`, `.planning/ROADMAP.md`, `.planning/STATE.md`, `.planning/config.json`.
- Subdirectories: `.planning/codebase/` for maps; `.planning/reference/` for `.planning/reference/SOURCES.md` and `.planning/reference/MILESTONE-OVERVIEW.md`; `.planning/phases/` for phase artifacts.
- Follow `AGENTS.md` when resolving sources; old checklist state does not establish current implementation state.

**`docs/superpowers/`:**
- Purpose: Day 1 milestone design and implementation sources.
- Key files: `docs/superpowers/specs/2026-10-20-day01-maf-erp-chatbot-design.md`, `docs/superpowers/plans/2026-10-21-day01-maf-erp-chatbot.md`.
- Whole-project intent comes from `tutorial/roadmap/Lo-trinh-MAF-cho-chatbot-ERP.pdf` according to `AGENTS.md`.

**`tutorial/`:**
- Purpose: Independent exercises and reference material outside the product solution.
- Contains: Standalone `Ex1`–`Ex4` projects under `tutorial/ngay-01/` and `tutorial/ngay-02/`.
- Key files: `tutorial/ngay-01/giai-thich-luong-tung-example.md`, `tutorial/ngay-01/Ex4/Program.cs`, `tutorial/ngay-02/Ex3/Program.cs`, `tutorial/ngay-02/Ex4/Program.cs`.
- Day 2 inventory/order/purchase functions use sample data; put product implementations under `code/src/`.

## Key File Locations

**Entry Points:**
- `code/src/ErpChatbot.Cli/Program.cs`: Product console entry point/composition root.
- `tutorial/ngay-01/Ex1/Program.cs` through `tutorial/ngay-01/Ex4/Program.cs`: Day 1 learning executables.
- `tutorial/ngay-02/Ex1/Program.cs` through `tutorial/ngay-02/Ex4/Program.cs`: Day 2 learning executables.

**Configuration:**
- `code/src/ErpChatbot.Cli/appsettings.json`: Required CLI settings file.
- `code/src/ErpChatbot.Cli/Program.cs`: Options binding, environment aliases and service lifetimes.
- `code/src/ErpChatbot.Core/Models/LlmOptions.cs`: Provider/model/generation/endpoint options shape.
- `code/src/ErpChatbot.Core/Models/ErpOptions.cs`: Assistant identity/instructions options shape.
- `code/ErpChatbot.sln`: Project/build membership.
- `.gitignore`: Build-output exclusions.
- `AGENTS.md`: Project workflow and documentation rules; no local `.codex/skills/` or `.agents/skills/` directory is detected.

**Core Logic:**
- `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`: Streaming/session/statistics orchestration.
- `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`: Provider client construction and readiness.
- `code/src/ErpChatbot.Cli/CliRunner.cs`: Local commands/classification.
- `code/src/ErpChatbot.Core/Models/ChatTurnResponse.cs`: Response record outside the current streaming path.

**Testing:**
- `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`: Fake client, history/usage/session tests.
- `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`: Fake client, provider/health tests.
- `code/tests/ErpChatbot.Tests/Cli/CliCommandTests.cs`: Fake agent and local-command tests.
- `code/tests/ErpChatbot.Tests/Core/CoreModelsTests.cs`: Defaults/statistics tests.

**Documentation:**
- `code/README.md`: Product run instructions; use source manifests for dependency facts.
- `.planning/codebase/ARCHITECTURE.md`: Layering/state/data flow.
- `.planning/reference/SOURCES.md`: Source reconciliation.

## Naming Conventions

**Files:**
- Use PascalCase type names, as in `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Prefix interfaces with `I`, as in `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`.
- Name tests after the subject plus `Tests`, as in `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`.
- Account for the existing exception: `code/src/ErpChatbot.Cli/CliRunner.cs` declares `CliCommandHandler`, not `CliRunner`.
- Date-prefix milestone documents, as in `docs/superpowers/specs/2026-10-20-day01-maf-erp-chatbot-design.md`; use uppercase filenames for `.planning/codebase/` maps.

**Directories:**
- Use `ErpChatbot.{Layer}` project names, as in `code/src/ErpChatbot.Infrastructure/`.
- Group implementation by responsibility (`Interfaces/`, `Models/`, `Factories/`, `Services/`); mirror layers under `code/tests/ErpChatbot.Tests/`.
- Keep learning exercises under day/`ExN` paths, as in `tutorial/ngay-02/Ex4/`.

## Where to Add New Code

**New Feature:**
- Put contracts in `code/src/ErpChatbot.Core/Interfaces/` and shared DTO/options in `code/src/ErpChatbot.Core/Models/`.
- Put orchestration in `code/src/ErpChatbot.Agent/Services/` and concrete external-service access in `code/src/ErpChatbot.Infrastructure/`.
- Wire dependencies in `code/src/ErpChatbot.Cli/Program.cs`; add matching layer tests under `code/tests/ErpChatbot.Tests/`.
- Extend option types and `code/src/ErpChatbot.Cli/appsettings.json` only as required by approved scope in `.planning/REQUIREMENTS.md`.

**New Component/Module:**
- Place provider factories alongside `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`; place services alongside `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Keep Agent independent of Infrastructure through `code/src/ErpChatbot.Core/Interfaces/` contracts.
- Add a project to `code/ErpChatbot.sln` only when scope requires a separate module/executable; tutorial projects are independent.

**New Route/Command:**
- Add terminal commands to `code/src/ErpChatbot.Cli/CliRunner.cs`, update hints in `code/src/ErpChatbot.Cli/Program.cs` and test in `code/tests/ErpChatbot.Tests/Cli/CliCommandTests.cs`.
- No application HTTP-route directory exists; API/auth/durable-session additions follow milestone decisions in `.planning/PROJECT.md`.

**Utilities:**
- No shared utility project/directory exists under `code/src/`; keep helpers within the owning layer.
- Keep test doubles/data in their existing test classes; no shared fixtures directory is established (`code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`, `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`).

## Special Directories

**`code/**/bin/`, `code/**/obj/`, `tutorial/**/bin/`, `tutorial/**/obj/`:**
- Purpose: .NET build/restore outputs; generated and excluded from source analysis.
- Generated: Yes.
- Committed: Ignored by `bin/` and `obj/` patterns in `.gitignore`.

**`.planning/graphs/`, `graphify-out/`:**
- Purpose: Generated graphs/reports outside application source; contents excluded from architecture analysis.
- Generated: Yes.
- Committed: Untracked in this snapshot; no explicit ignore rule appears in `.gitignore`.

**`.planning/phases/`:**
- Purpose: Workflow-managed phase planning/execution artifacts; phase folders contain `.gitkeep` placeholders.
- Generated: Workflow-managed.
- Committed: Untracked in this snapshot; preserve concurrent workflow changes.

**`tutorial/ngay-02/`, `tutorial/ref/`, `tutorial/roadmap/`:**
- Purpose: Learning samples and source/reference documents.
- Generated: No application generation detected.
- Committed: Untracked in this snapshot; presence does not imply integration into `code/ErpChatbot.sln`.

---

*Structure analysis: 2026-10-23*
