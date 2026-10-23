---
last_mapped_commit: 77ef075b9c1be6df3639fa35df7014a9e493fe18
---

# Testing Patterns

**Analysis Date:** 2026-10-23

## Test Framework

**Runner:**
- xUnit 2.9.3, `xunit.runner.visualstudio` 3.1.4, and `Microsoft.NET.Test.Sdk` 17.14.1 in `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`.
- The project targets `net10.0`, globally imports `Xunit`, and references all four application projects. No separate runsettings, test-collection configuration, or runner JSON detected.

**Assertion Library:**
- FluentAssertions 8.0.1 in `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`.
- Match the `Should().Be`, `Contain`, `NotContain`, `HaveCount`, `BeOfType`, `Throw`, and `ThrowAsync` assertions in `code/tests/ErpChatbot.Tests/`.

**Run Commands:**
Run these from the repository root; these commands are guidance, not execution evidence for this map.

```bash
dotnet test code/ErpChatbot.sln
dotnet watch --project code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj test
dotnet test code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj --filter FullyQualifiedName~ChatClientFactoryTests
dotnet test code/ErpChatbot.sln --collect:"XPlat Code Coverage"
```

- `code/README.md` documents `dotnet test` from `code/`; test sources are inspected statically here. Its statement of a 100% passing suite is not independently verified by this map.

## Test File Organization

**Location:**
- Use the separate `code/tests/ErpChatbot.Tests/` tree, organized by application layer, matching `code/src/`.

**Naming:**
- Use `SubjectTests.cs` and public `[Fact]` methods with descriptive underscore-separated names; see `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`.
- All 21 detected cases use `[Fact]`: 3 Core, 6 Infrastructure, 7 Agent, and 5 CLI. No theories, snapshots, or dedicated live integration suite detected in `code/tests/ErpChatbot.Tests/`.

**Structure:**
```text
code/tests/ErpChatbot.Tests/
├── ErpChatbot.Tests.csproj
├── Core/CoreModelsTests.cs
├── Infrastructure/ChatClientFactoryTests.cs
├── Agent/ErpAgentServiceTests.cs
└── Cli/CliCommandTests.cs
```

## Test Structure

**Suite Organization:**
Use one class per subject, construct fresh subject/options/fakes inside each test, then assert public results. Example from `code/tests/ErpChatbot.Tests/Core/CoreModelsTests.cs`:

```csharp
[Fact]
public void SessionStats_ShouldAccumulateCorrectly()
{
    var stats = new SessionStats();
    stats.RecordTurn(inputTokens: 10, outputTokens: 25);
    stats.RecordTurn(inputTokens: 20, outputTokens: 35);

    stats.TurnCount.Should().Be(2);
    stats.TotalInputTokens.Should().Be(30);
    stats.TotalOutputTokens.Should().Be(60);
    stats.TotalTokens.Should().Be(90);
}
```

**Patterns:**
- Keep arrange/act/assert separated with whitespace; scenario comments in `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs` describe multi-turn operations.
- No shared fixture lifecycle, base test class, constructor setup, or collection fixture detected in `code/tests/ErpChatbot.Tests/`.
- Restore process-global output in `finally`, following `ProcessInput_WithInfoCommand_DisplaysConfiguration` in `code/tests/ErpChatbot.Tests/Cli/CliCommandTests.cs`. `Console.SetOut` is global and can capture output from other concurrently executing classes; no explicit serialization configuration is present.

## Mocking

**Framework:**
- Handwritten nested fake classes; no mocking framework package in `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`.

**Patterns:**
The health fake delegates completion behavior to a callback, and the production factory accepts it through an existing constructor seam in `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`:

```csharp
var fakeClient = new FakeChatClient((msgs, opt, ct) =>
    Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong"))));
var factory = new ChatClientFactory(options, _ => fakeClient);

var (isHealthy, message) = await factory.CheckHealthAsync();

isHealthy.Should().BeTrue();
message.Should().Contain("sẵn sàng");
```

- `FakeChatClient` implements completion and throws for unused streaming in `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`.
- `FakeStreamingChatClient` records `List<List<ChatMessage>>` history, forwards cancellation during enumeration, and delegates chunk generation in `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`.
- `FakeAgentService` tracks reset/prompt calls and owns an actual `SessionStats` in `code/tests/ErpChatbot.Tests/Cli/CliCommandTests.cs`.

**What to Mock:**
- Replace the network-facing `IChatClient` boundary for health/stream behavior, following the Infrastructure and Agent test files above. These tests do not require a running Ollama server.
- Replace `IErpAgentService` when checking local command disposition in `code/tests/ErpChatbot.Tests/Cli/CliCommandTests.cs`.

**What NOT to Mock:**
- Exercise actual `SessionStats`, option defaults, and the real subject under test in `code/tests/ErpChatbot.Tests/Core/CoreModelsTests.cs`.
- Agent tests exercise the real `ErpAgentService` and its MAF session wrapper around the fake client, rather than replacing the agent itself; see `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`.

## Fixtures and Factories

**Test Data:**
- Use inline options with `Options.Create(new ErpOptions())` or `Options.Create(new LlmOptions { ... })` in `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs` and `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`.
- Reuse private iterator helpers for stream text and usage in `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`:

```csharp
private static async IAsyncEnumerable<ChatResponseUpdate> CreateChunks(string[] chunks)
{
    foreach (var chunk in chunks)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
        await Task.Yield();
    }
}
```

- `CreateChunksWithUsage` appends a separate `UsageContent(new UsageDetails { ... })` update after text chunks in the same file; model token accounting through that content path.

**Location:**
- Fixtures/fakes/helpers are private nested declarations in individual files under `code/tests/ErpChatbot.Tests/`; no shared fixture-data directory detected.

## Coverage

**Requirements:**
- No enforced percentage or coverage threshold detected in `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`; no CI coverage gate detected.
- Presence of 21 tests and README passing claims is not evidence of execution or measured coverage; see `code/README.md` and the acceptance conditions in `.planning/REQUIREMENTS.md`.

**Configuration:**
- `coverlet.collector` 6.0.4 is referenced in `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`. No custom exclusions, report generator, or committed runsettings detected.

**View Coverage:**
```bash
dotnet test code/ErpChatbot.sln --collect:"XPlat Code Coverage"
```

- Inspect the resulting Cobertura report under the test project's `TestResults/` directory; no repository HTML coverage-view command detected in `code/README.md`.

## Test Types

**Unit Tests:**
- Core tests check option defaults, ERP persona text, and accumulated counters in `code/tests/ErpChatbot.Tests/Core/CoreModelsTests.cs`.
- Factory tests check provider construction/rejection and health success, missing model, timeout, and unreachable endpoint in `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`.
- Command tests check reset, stats routing, info output, invalid slash commands, and whitespace input in `code/tests/ErpChatbot.Tests/Cli/CliCommandTests.cs`.

**Integration Tests:**
- Agent tests combine the actual MAF adapter/session with a fake chat client for stream order, multi-turn context, usage, reset, failure/cancellation recovery, and two-conversation isolation in `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`.
- The factory construction test checks the actual configured wrapper and Ollama client registration without issuing an actual HTTP request in `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`.
- No executable-host/DI/config binding or live HTTP integration suite detected for `code/src/ErpChatbot.Cli/Program.cs`.

**E2E Tests:**
- No automated CLI process or live Ollama E2E suite detected. `code/README.md` describes a manual live run; `.planning/REQUIREMENTS.md` requires actual live multi-turn evidence separately.

## Common Patterns

**Async Testing:**
- Use `public async Task` tests and fully enumerate streams to trigger execution and final counters in `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`:

```csharp
await foreach (var _ in service.RunStreamingAsync("Lượt 1")) { }
await foreach (var _ in service.RunStreamingAsync("Lượt 2")) { }
service.GetStats().TurnCount.Should().Be(2);
```

**Error Testing:**
- Wrap stream enumeration in an async delegate; assert exception type, then submit another turn and inspect retained history in `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`:

```csharp
var act = async () =>
{
    await foreach (var _ in service.RunStreamingAsync("Gặp lỗi nè")) { }
};
await act.Should().ThrowAsync<HttpRequestException>();
```

- Health timeout uses a cancellation-aware `Task.Delay(10000, ct)` with the factory timeout reduced to 50 ms in `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`; avoid a real five-second wait when extending that scenario.

**Snapshot Testing:**
- Not used in `code/tests/ErpChatbot.Tests/`; assertions check concrete state, types, counters, and message fragments.

**Coverage Gaps:**
- Cancellation is requested before the second call, and the network fake throws before yielding text. Neither test verifies cancellation or network failure after partial stream output, pending HTTP interruption, or completed-turn counters after interruption: `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`.
- CLI tests omit exit/quit/help aliases, case/trim variants, and normal text disposition; stats tests do not assert printed totals and invalid-command tests do not assert hint text: `code/tests/ErpChatbot.Tests/Cli/CliCommandTests.cs`.
- Factory tests assert wrapper construction but do not observe temperature/output limits on a model request; health payload/one-token override, caller cancellation, invalid endpoint, and fallback error classification are unasserted: `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`.
- Environment precedence, actual scope registration, console EOF/Ctrl+C handling, and streaming output formatting in `code/src/ErpChatbot.Cli/Program.cs` have no automated host-level tests.
- Persona tests check configured text rather than a model's Vietnamese/no-fabrication behavior: `code/tests/ErpChatbot.Tests/Core/CoreModelsTests.cs`. Fake responses cannot establish live model answer quality.

---

*Testing analysis: 2026-10-23*
