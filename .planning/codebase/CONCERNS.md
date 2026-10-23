---
last_mapped_commit: 77ef075b9c1be6df3639fa35df7014a9e493fe18
---

# Codebase Concerns

**Analysis Date:** 2026-10-23

**Review scope:** Bounded static review of production code in `code/src/`, tests in `code/tests/`, package manifests in `tutorial/`, and active requirements in `.planning/REQUIREMENTS.md`. No builds, test execution, live Ollama requests, dependency audit, or generated-output inspection. Code observations below are distinguished from unverified runtime risks.

## Tech Debt

**Configuration has no validation boundary:**
- Issue: `LlmOptions` is bound and overridden without options validation; provider normalization, URI construction, and numeric generation settings are left to later execution.
- Files: `code/src/ErpChatbot.Core/Models/LlmOptions.cs`, `code/src/ErpChatbot.Cli/Program.cs`, `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.
- Why: The implementation uses simple mutable options with usable local defaults; no rationale for omitting validation is documented.
- Impact: Malformed endpoints and unsupported providers can fail during dependency resolution; invalid temperature/output limits reach the provider instead of a configuration diagnostic.
- Fix approach: Validate provider, model, endpoint, and supported numeric ranges before creating the conversation service; retain the explicit Ollama-only rejection.

**README assertions exceed the available verification evidence:**
- Issue: The README calls the fake-client tests comprehensive, states a 100% pass rate, and describes Core as having no third-party AI dependency. Core directly references `Microsoft.Extensions.AI.Abstractions` and exposes `IChatClient`.
- Files: `code/README.md`, `code/src/ErpChatbot.Core/ErpChatbot.Core.csproj`, `code/src/ErpChatbot.Core/Interfaces/IChatClientFactory.cs`, `.planning/REQUIREMENTS.md`.
- Why: The text combines architectural intent and verification claims without linking reproducible execution evidence.
- Impact: A maintainer can mistake fake-client coverage for actual provider verification or incorrectly plan a dependency-free Core change.
- Fix approach: Document the abstraction exception and attach dated build/test/live-smoke evidence; keep unverified requirements pending.

## Known Bugs

**Startup configuration exceptions bypass the friendly CLI error path:**
- Symptoms: Static control flow permits an unhandled exception before the banner and health diagnostic.
- Trigger: Launch with an unsupported `LLM_PROVIDER` or malformed `OLLAMA_ENDPOINT`; dependency resolution calls `CreateClient()` before `CheckHealthAsync()` and outside the chat-loop try/catch. Runtime reproduction is not performed in this map.
- Files: `code/src/ErpChatbot.Cli/Program.cs`, `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.
- Workaround: Use a supported provider and valid absolute endpoint configuration.
- Root cause: Eager resolution of `IErpAgentService` creates the client before startup error handling.
- Fix approach: Validate configuration or catch startup resolution errors and return a clear diagnostic with an appropriate exit status.

**Caller cancellation is converted into an unhealthy result:**
- Symptoms: `CheckHealthAsync` does not propagate caller-triggered `OperationCanceledException`; the generic catch returns a connection-check error message.
- Trigger: Call `CheckHealthAsync` with a canceled token while the internal timeout token is not canceled, using a client that observes cancellation. This follows the catch ordering; no runtime reproduction is claimed.
- Files: `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`, `code/src/ErpChatbot.Core/Interfaces/IChatClientFactory.cs`.
- Workaround: Current CLI startup supplies no cancellation token, so that caller does not exercise this path.
- Root cause: Only timeout cancellation gets a dedicated catch; all other exceptions are converted to results.
- Fix approach: Propagate cancellation requested by the caller before applying general health-error classification, and cover caller cancellation separately from timeout.

## Security Considerations

**Endpoint configuration controls where conversation text is sent:**
- Risk: A configured remote or plain-HTTP endpoint receives prompts and conversation history; selecting the Ollama provider alone does not guarantee that data stays on the workstation.
- Files: `code/src/ErpChatbot.Core/Models/LlmOptions.cs`, `code/src/ErpChatbot.Cli/Program.cs`, `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`.
- Current mitigation: The default endpoint is local, the application supports only Ollama, and the CLI shows the endpoint. No HTTP server or untrusted endpoint-editing API is implemented here.
- Recommendations: Treat endpoint configuration as trusted operator input. Before entering real ERP data, approve the deployment endpoint and transport under the data policy in `.planning/REQUIREMENTS.md`; do not describe local-provider selection as a network-isolation guarantee.

**Conversation state requires one owner and serialized access:**
- Risk: Sharing an `ErpAgentService` instance between users exposes history; concurrent run/reset calls on one instance race over `_session` and mutable statistics. Concurrent execution is an untested risk, not an observed CLI failure.
- Files: `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`, `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`, `code/src/ErpChatbot.Cli/Program.cs`.
- Current mitigation: The interface documents conversation ownership, DI registers the service as scoped, and the CLI uses one sequential loop in one scope.
- Recommendations: Preserve the current ownership rule. A future web scope must be tied to the authenticated conversation lifecycle and serialize operations on that conversation; a per-request scope alone does not provide durable conversation ownership.

## Performance Bottlenecks

**Conversation history has no application-defined bound:**
- Problem: The service retains one session until `/reset`, with no visible trimming, summarization, input-size limit, or context budget in application code.
- Files: `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`, `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`, `code/src/ErpChatbot.Cli/CliRunner.cs`.
- Measurement: Not measured. Tests establish that prior turns are included in subsequent requests; no memory, latency, or context-limit benchmark is present in the reviewed tests.
- Cause: Context retention is delegated to the framework session without an application retention policy.
- Improvement path: Use `/reset` for the current CLI. Measure realistic conversations before choosing bounded history or summarization; preserve explicit reset and session statistics semantics.

**The five-second startup probe also performs model inference:**
- Problem: The probe times out after the same five-second budget whether the model is unreachable, warming up, or generating its first response.
- Files: `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`, `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`.
- Measurement: The configured production timeout is 5000ms; the timeout test uses 50ms with a fake. Actual cold-start latency is not measured.
- Cause: Readiness is checked using a one-output-token response request, as required by `.planning/REQUIREMENTS.md`.
- Improvement path: Keep the required timeout, but verify cold/warm behavior against the selected model and document that a timeout is not proof of a missing model. The CLI currently allows retrying a chat after the warning.

## Fragile Areas

**Health failure classification relies on broad exception-message matching:**
- Files: `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`, `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`.
- Why fragile: Any exception containing `not found` or `pull` is classified as a missing model before the `HttpRequestException` catch runs.
- Common failures: A proxy endpoint returning an unrelated not-found message can get model-download advice; provider wording changes can lose the intended classification. These are untested cases, not observed incidents.
- Safe modification: Prefer provider status/error information where available; otherwise constrain matching to model-specific messages and retain connection/timeout distinctions.
- Test coverage: Tests use one fabricated missing-model message and one connection-refused exception; ambiguous HTTP failures and other response statuses are absent.

**Token accounting depends on complete stream consumption and usage event shape:**
- Files: `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`, `code/src/ErpChatbot.Core/Models/SessionStats.cs`, `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`.
- Why fragile: All usage counts are summed and committed only after enumeration ends normally. Errors, cancellation, or an early consumer break skip `RecordTurn`, even after text or usage is emitted.
- Common failures: Statistics omit interrupted work; multiple cumulative usage events would overcount if a provider emits that shape. The latter is a provider-contract risk, not a verified Ollama defect.
- Safe modification: Define whether the CLI reports completed turns or all incurred usage, then test the intended policy with usage-only, absent-usage, multiple-usage, and interrupted streams before changing it.
- Test coverage: Successful accumulation uses exactly one final usage event per turn; interrupted usage and zero-usage reporting are not asserted.

## Scaling Limits

**Single sequential, in-memory CLI conversation:**
- Files: `code/src/ErpChatbot.Cli/Program.cs`, `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`.
- Current capacity: One active conversation service and one turn at a time per CLI process; no throughput or concurrent-user capacity has been measured.
- Limit: History and statistics live only in process memory, with no durable session store or cross-process coordination.
- Symptoms at limit: Process exit discards conversation state; long conversations face model/context and memory constraints with no application-defined bound.
- Scaling path: Keep the CLI serial. Design persistence, identity, and concurrency together only when implementing the future web milestone in `.planning/REQUIREMENTS.md`.

## Dependencies at Risk

**Provider/framework integration is tested through fakes:**
- Files: `code/src/ErpChatbot.Agent/ErpChatbot.Agent.csproj`, `code/src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj`, `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`, `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`.
- Risk: Pinned versions do not establish actual model streaming, cancellation, or usage compatibility. No package is declared vulnerable, deprecated, or unavailable by this static map.
- Impact: Fake-based tests can keep passing while real provider responses or framework behavior differ.
- Migration plan: Preserve the fake seams and run the required real-Ollama smoke flow before dependency changes are accepted; verify the API against current documentation during implementation.

## Missing Critical Features

- No additional feature is classified as missing from the current milestone solely because the product roadmap contains it. ERP tools, web authentication, persisted history, telemetry, and write approvals are explicitly future scope in `.planning/REQUIREMENTS.md`; their absence from `code/src/` is not a v0.1 defect.
- The required live-provider verification remains an acceptance concern: `code/README.md` supplies manual instructions but does not establish a dated multi-turn/streaming smoke result. Record model, environment, commands, and outcome for `VERIFY-01` in `.planning/REQUIREMENTS.md`; do not infer completion from source or fake tests.

## Test Coverage Gaps

**Cancellation and errors after a partial response:**
- Files: `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`, `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`, `code/src/ErpChatbot.Cli/Program.cs`.
- What's not tested: Cancellation during an awaited provider read, a client emitting text and then throwing, retained statistics after either event, and the CLI's separate-line error/cancellation output.
- Risk: Pre-canceled-token and immediately-throwing fake cases do not prove `RES-01` through `RES-03` behavior during an active stream.
- Priority: High.
- Difficulty to test: A controllable async fake must expose when enumeration is blocked or text has been yielded, then observe cancellation/error and the following turn.

**Composition root and effective generation configuration:**
- Files: `code/src/ErpChatbot.Cli/Program.cs`, `code/tests/ErpChatbot.Tests/Cli/CliCommandTests.cs`, `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`.
- What's not tested: Startup DI resolution, JSON/environment precedence, malformed configuration, normal chat and exit routing, or the effective temperature/output settings received by the underlying client.
- Risk: Direct handler tests bypass actual startup; the factory construction test checks wrapper type rather than the request options required by `CONF-03` in `.planning/REQUIREMENTS.md`.
- Priority: High.
- Difficulty to test: Environment and console state are process-wide; isolate these tests or launch the CLI as a child process. Capture client options through the existing fake-client seam where practical.

**Real-model persona and no-data behavior:**
- Files: `code/src/ErpChatbot.Core/Models/ErpOptions.cs`, `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`, `.planning/REQUIREMENTS.md`.
- What's not tested: Whether the configured model consistently answers in Vietnamese and refuses to invent ERP numbers when no tools/data are connected.
- Risk: A prompt instruction is not behavioral verification for `CHAT-01` and `CHAT-04`; fake clients return predetermined strings.
- Priority: High for milestone acceptance.
- Difficulty to test: Requires the real model and representative prompts; document manual results or a bounded behavioral evaluation separately from unit tests.

---

*Concerns audit: 2026-10-23*
*Update as issues are fixed or new ones discovered.*
