# Project Agent Instructions

<!-- context7 -->
Use the `ctx7` CLI to fetch current documentation whenever the user asks about a library, framework, SDK, API, CLI tool, or cloud service, including API syntax, configuration, migration, library-specific debugging, setup and CLI usage. Prefer it over web search for library docs, even when the API seems familiar.

Do not use it for refactoring, scripts from scratch, business-logic debugging, code review, or general programming concepts.

1. Resolve first: `npx ctx7@latest library <official-name> "<specific single-concept question>"`. Choose the best relevant `/org/project` ID using exact name, reputation, snippet count and benchmark score; retry alternate names only if needed.
2. Fetch: `npx ctx7@latest docs <libraryId> "<specific question>"`. Use a separate docs command per concept unless asking how concepts interact. Use a versioned ID from library output for version-specific docs.
3. Answer from the fetched docs. Always call `library` first unless the user supplied a valid `/org/project` ID. Do not exceed 3 commands per question or put credentials/private data in queries.

On quota errors, report them and suggest `npx ctx7@latest login` or `CONTEXT7_API_KEY`; do not silently fall back to training data. Run requests outside the default sandbox where permitted; if DNS/network errors occur, rerun outside the sandbox rather than repeating inside it. Respect the current environment's permission policy when no escalation is available.
<!-- context7 -->

## Planning source hierarchy

- Whole project: `tutorial/roadmap/Lo-trinh-MAF-cho-chatbot-ERP.pdf` (7 stages, 0–6).
- First milestone only: the Day 1 design and implementation plan under `docs/superpowers/`.
- Active GSD scope: `.planning/PROJECT.md`, `.planning/REQUIREMENTS.md`, `.planning/ROADMAP.md` and `.planning/STATE.md`.
- Future milestones/source reconciliation: `.planning/reference/`.
- Refresh Git/code state before executing old task lists. Do not overwrite work from another active session or infer passing tests from file existence.

<!-- GSD:project-start source:PROJECT.md -->

## Project

**MAF-Chatbot ERP**

Trợ lý ERP cho người dùng hỏi bằng tiếng Việt và nhận câu trả lời dựa trên dữ liệu ERP trong đúng quyền của mình. Dịch vụ agent ASP.NET Core/.NET 10 đứng riêng với ERP hiện hữu, gọi các tool nghiệp vụ qua API thay vì để mô hình truy vấn database tùy ý. Dự án phát triển theo 7 giai đoạn 0–6 trong PDF lộ trình; milestone đầu tiên hiện thực nền tảng và agent console trước khi nối dữ liệu thật.

**Core Value:** Người dùng nhận được câu trả lời đúng từ ERP trong phạm vi quyền của mình; khi không có dữ liệu, trợ lý nói rõ thay vì bịa số.

### Constraints

- **Runtime**: .NET 10 cho code mới; giữ ERP .NET Framework riêng. PDF cho phép .NET 8/10, design v0.1 đã chọn .NET 10.
- **Provider v0.1**: Ollama duy nhất, cấu hình `LLM_PROVIDER`, `LLM_MODEL`, `OLLAMA_ENDPOINT`; provider khác phải bị từ chối rõ ràng. Provider cloud chỉ quyết định sau khi rõ chính sách dữ liệu.
- **Architecture**: Core, Infrastructure, Agent, CLI và Tests; Agent không phụ thuộc Infrastructure. Composition root ở CLI.
- **Core abstraction**: design ghi Core không có thư viện AI bên thứ ba nhưng `IChatClientFactory` trả `IChatClient`. Baseline thực tế dùng `Microsoft.Extensions.AI.Abstractions`; giữ seam abstraction và ghi nhận ngoại lệ này, không coi Core hoàn toàn không có dependency AI.
- **Conversation ownership**: một service instance cho một conversation; web tương lai phải kiểm tra quyền sở hữu phiên và tránh singleton chứa lịch sử dùng chung.
- **Health**: request thật xác nhận model, timeout 5 giây, phân biệt mất kết nối/thiếu model/timeout.
- **Streaming**: token lấy qua `UsageContent` trong `AgentResponseUpdate.Contents`; Ctrl+C và lỗi giữa stream không tự reset session. `/reset` giữ thống kê của phiên CLI.
- **Versions**: phiên bản trong tài liệu và `.csproj` là baseline được pin, chưa phải khẳng định API mới nhất; xác minh qua Context7 trước khi dùng API hoặc sửa cấu hình SDK.

<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->

## Technology Stack

| Component | Baseline | Evidence / caveat |
|-----------|----------|-------------------|
| Runtime | .NET 10 / `net10.0` | Các `.csproj` hiện có |
| Core chat abstraction | Microsoft.Extensions.AI.Abstractions 10.10.0 | Core `.csproj`; khác phát biểu "không dependency AI" trong design |
| Chat integration | Microsoft.Extensions.AI 10.10.0 | Infrastructure và Agent WIP `.csproj` |
| Agent framework | Microsoft.Agents.AI 1.24.0 | Agent WIP `.csproj`; phải kiểm tra package availability/API khi plan implementation |
| Local provider | OllamaSharp 5.5.0 | Infrastructure `.csproj` |
| Options / hosting | Microsoft.Extensions.Options 10.0.0; Hosting theo plan CLI | Hosting chưa được coi đã triển khai |
| Testing | xUnit, FluentAssertions | Test project hiện có; fake chat clients, không cần Ollama để chạy unit tests |
| Future ERP host | ASP.NET Core | PDF; chưa có endpoint, auth hay DB conversation production |
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->

## Conventions

Conventions not yet established. Will populate as patterns emerge during development.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->

## Architecture

## Layout

- `code/ErpChatbot.sln`: solution mới, .NET 10.
- `code/src/ErpChatbot.Core`: options, stats, response model, `IErpAgentService`, `IChatClientFactory`.
- `code/src/ErpChatbot.Infrastructure`: Ollama `ChatClientFactory`, health probe và DI options.
- `code/src/ErpChatbot.Agent`: tại lần đọc đầu có project chưa track và test WIP; implementation có thể thay đổi bởi phiên khác.
- `code/tests/ErpChatbot.Tests`: Core tests và Infrastructure tests đã commit; Agent tests WIP.
- `tutorial/ngay-01`: ví dụ học; không coi là ứng dụng giao cho người dùng.

## Intended dependency direction

## Existing contracts

- `IErpAgentService.RunStreamingAsync(string, CancellationToken)` trả `IAsyncEnumerable<string>`; `ResetSessionAsync()`; `GetStats()`.
- `IChatClientFactory.CreateClient()` trả `IChatClient`; `CheckHealthAsync(CancellationToken)` trả `(bool IsHealthy, string Message)`.
- `SessionStats.RecordTurn` cộng lượt và token vào thống kê tích lũy.
- Core hiện có `Microsoft.Extensions.AI.Abstractions` để biểu diễn `IChatClient`; không tham chiếu provider cụ thể.

## Baseline evidence

- `bd1051b`: Core bootstrap/models/interfaces + 3 Core test methods.
- `ad09bbd`: Infrastructure factory + 6 test methods cho provider và health ready/missing/timeout/unreachable.
- `8dc0574`: Agent service và test được commit trong lúc khởi tạo planning, sau snapshot WIP đầu tiên; chưa xác minh kết quả thực thi.
- Chỉ xác minh sự tồn tại và nội dung file; chưa chạy build/test trong phiên này. Không đánh dấu requirement complete.
- Code WIP phải được refresh khi plan-phase bắt đầu. Không tạo lại solution/project hoặc ghi đè thay đổi từ phiên khác theo plan cũ.

<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->

## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->

## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:

- `/gsd:quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd:debug` for investigation and bug fixing
- `/gsd:execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->

## Developer Profile

> Profile not yet configured. Run `/gsd:profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
