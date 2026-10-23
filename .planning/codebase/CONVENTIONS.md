---
last_mapped_commit: 77ef075b9c1be6df3639fa35df7014a9e493fe18
---

# Coding Conventions

**Analysis Date:** 2026-10-23

## Naming Patterns

**Files:**
- Use PascalCase matching the primary type, as in `code/src/ErpChatbot.Core/Models/SessionStats.cs` and `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Note the existing exception: `code/src/ErpChatbot.Cli/CliRunner.cs` contains `CliCommandHandler` and `CliInputResult`, rather than a `CliRunner` type.
- Name test files `SubjectTests.cs` under the matching layer directory, as in `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`.

**Functions:**
- Use PascalCase methods and append `Async` to asynchronous operations: `CheckHealthAsync`, `RunStreamingAsync`, `ResetSessionAsync` in `code/src/ErpChatbot.Core/Interfaces/`.
- Use descriptive underscore-separated test names, e.g. `RunStreamingAsync_RetainsContextAcrossConsecutiveTurns` in `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`.

**Variables:**
- Use camelCase parameters and locals, and `_camelCase` private fields; see `_agent`, `_session`, and `turnInputTokens` in `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Use PascalCase properties and constants, e.g. `TotalTokens` and `SectionName` in `code/src/ErpChatbot.Core/Models/SessionStats.cs` and `code/src/ErpChatbot.Core/Models/LlmOptions.cs`.

**Types:**
- Prefix interfaces with `I`, as in `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`; use PascalCase classes, records, and enum members.
- Use mutable classes for configuration, a record for response data, and a class with private setters for counters; see `code/src/ErpChatbot.Core/Models/LlmOptions.cs`, `ChatTurnResponse.cs`, and `SessionStats.cs`.

## Code Style

**Formatting:**
- Match the four-space indentation, braces on separate lines, semicolons, double-quoted strings, and file-scoped namespaces in `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Prefer `var` where the initializer conveys the type; existing explicit types also clarify counters and nullable input in `code/src/ErpChatbot.Cli/Program.cs`.
- Use collection expressions (`[]`, `[testMessage]`), target-typed `new`, and expression-bodied simple members where surrounding code does; examples are in `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs` and `code/src/ErpChatbot.Core/Models/SessionStats.cs`.
- No repository formatter configuration or enforced line width detected; project XML uses two-space indentation in `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj`.

**Linting:**
- No dedicated lint configuration, `.editorconfig`, custom analyzer configuration, or format command detected. Nullable reference types and implicit usings are enabled in `code/src/ErpChatbot.Core/ErpChatbot.Core.csproj` and `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`.

## Import Organization

**Order:**
1. Explicit `System.*` namespaces when needed, e.g. `System.Runtime.CompilerServices` in `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
2. Internal `ErpChatbot.*` namespaces.
3. External `Microsoft.*`, `OllamaSharp`, and test libraries; see `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.

**Grouping:**
- Keep a contiguous using block before the file-scoped namespace, as in `code/src/ErpChatbot.Cli/CliRunner.cs`; no enforced sorting configuration detected.
- Common system namespaces are implicit; xUnit also has an explicit project-level global using in `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`.

**Path Aliases:**
- Not detected. Use namespace imports and explicit project references, as in `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj`.

## Error Handling

**Patterns:**
- Return early for ignored input: whitespace prompts yield no stream in `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`, and empty CLI input returns `CliInputResult.Ignored` in `code/src/ErpChatbot.Cli/CliRunner.cs`.
- Let stream failures propagate to the CLI boundary; preserve the conversation session across exceptions and cancellation in `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Handle cancellation, network failures, and generic failures separately in `code/src/ErpChatbot.Cli/Program.cs`; remove the per-turn cancel handler in `finally`.

**Error Types:**
- Unsupported providers throw `InvalidOperationException`; expected health failures return `(bool IsHealthy, string Message)` in `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.
- Health classification uses catch filters for timeout and model-related messages, then `HttpRequestException` and a generic fallback in `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`. Preserve these distinct user messages when editing this boundary.
- No custom exception hierarchy is present in `code/src/ErpChatbot.Core/Interfaces/` or the implementations above.

## Logging

**Framework:**
- Application messages use `Console.Write`/`Console.WriteLine` and console colors in `code/src/ErpChatbot.Cli/Program.cs` and `code/src/ErpChatbot.Cli/CliRunner.cs`; no application `ILogger` calls detected.

**Patterns:**
- Keep output and localized Vietnamese diagnostics at the CLI boundary; return diagnostic text from `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs` rather than printing there.
- Set UTF-8 input/output and restore console colors after status messages, following `code/src/ErpChatbot.Cli/Program.cs`.

## Comments

**When to Comment:**
- Explain session ownership, token extraction, cancellation, and configuration decisions with Vietnamese comments, matching `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs` and `code/src/ErpChatbot.Cli/Program.cs`.
- Educational files use numbered explanatory steps; see `tutorial/ngay-01/Ex1/Program.cs` and `tutorial/ngay-02/Ex1/Program.cs`. Treat these as examples rather than production-layer conventions.

**JSDoc/TSDoc:**
- Not applicable to this C# codebase. XML documentation in `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs` records the one-instance-per-conversation rule and prohibits shared singleton history in a web host.
- Public XML comments are selective rather than universally present; `code/src/ErpChatbot.Core/Interfaces/IChatClientFactory.cs` has signatures without XML documentation.

**TODO Comments:**
- No standard TODO ownership or issue-link format detected in the inspected production and test files under `code/`.

## Function Design

**Size:**
- No enforced function-size limit detected. Keep compact model operations such as `RecordTurn` in `code/src/ErpChatbot.Core/Models/SessionStats.cs`; command routing is one explicit method in `code/src/ErpChatbot.Cli/CliRunner.cs`.

**Parameters:**
- Inject interfaces and `IOptions<T>` through constructors, following `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Put optional `CancellationToken cancellationToken = default` last; mark async-iterator tokens with `[EnumeratorCancellation]` in `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Use explicit test seams already present: a delegate and configurable timeout in `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`, and a plain-options constructor in `code/src/ErpChatbot.Cli/CliRunner.cs`.

**Return Values:**
- Stream user-facing text as `IAsyncEnumerable<string>`, health as a named tuple, and command disposition as `CliInputResult`; see `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`, `code/src/ErpChatbot.Core/Interfaces/IChatClientFactory.cs`, and `code/src/ErpChatbot.Cli/CliRunner.cs`.
- Update accumulated counters only after successful stream completion; `/reset` replaces session history while retaining counters in `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.

## Module Design

**Exports:**
- Use public C# interfaces/classes and namespace-per-layer organization; `code/src/ErpChatbot.Core/Interfaces/` defines the service seams consumed by implementations.
- Keep dependency registration in `code/src/ErpChatbot.Cli/Program.cs`, with scoped agent/command handler and a chat-client factory; retain the per-conversation service ownership documented in `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`.
- Consult actual `.csproj` references: `code/src/ErpChatbot.Core/ErpChatbot.Core.csproj` references AI abstractions, so Core is not entirely AI-dependency-free despite the statement in `code/README.md`.

**Barrel Files:**
- Not applicable. C# project references and namespaces provide module visibility; see `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj`.

---

*Convention analysis: 2026-10-23*
