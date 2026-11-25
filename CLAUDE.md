@AGENTS.md

## Build & test
- Build: `dotnet build code/ErpChatbot.sln`
- Test: `dotnet test code/ErpChatbot.sln`
- Skill `/dotnet-verify` chạy cả hai.

## Kiến trúc
Core ← Infrastructure, Agent; CLI là composition root. Agent không phụ thuộc Infrastructure.
Secret: `appsettings.Local.json` (gitignored), hook chặn sửa.
