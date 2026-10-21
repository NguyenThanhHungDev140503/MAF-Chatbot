# Stack baseline

**Surveyed:** 2026-10-21. Nguồn là `.csproj` hiện có và design/plan, không phải xác minh tài liệu SDK hiện tại.

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

Khi hỏi/triển khai API thư viện, chạy `npx ctx7@latest library ...` rồi `docs ...` theo chỉ dẫn dự án. Nếu package/version ở nguồn không resolve được thì ghi blocker và xác minh lại, không tự khẳng định ví dụ đã chạy.
