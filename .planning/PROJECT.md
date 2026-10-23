# MAF-Chatbot ERP

## What This Is

Trợ lý ERP cho người dùng hỏi bằng tiếng Việt và nhận câu trả lời dựa trên dữ liệu ERP trong đúng quyền của mình. Dịch vụ agent ASP.NET Core/.NET 10 đứng riêng với ERP hiện hữu, gọi các tool nghiệp vụ qua API thay vì để mô hình truy vấn database tùy ý. Dự án phát triển theo 7 giai đoạn 0–6 trong PDF lộ trình; milestone đầu tiên hiện thực nền tảng và agent console trước khi nối dữ liệu thật.

## Core Value

Người dùng nhận được câu trả lời đúng từ ERP trong phạm vi quyền của mình; khi không có dữ liệu, trợ lý nói rõ thay vì bịa số.

## Requirements

### Validated

Chưa có khả năng sản phẩm được xác minh trong phiên khởi tạo GSD này. Code và test đã tồn tại là baseline để tiếp tục, không đồng nghĩa milestone đã nghiệm thu.

### Active

- [ ] Milestone v0.1: chạy console nhiều lượt bằng MAF `AIAgent`/`AgentSession`, trả lời streaming tiếng Việt qua Ollama.
- [ ] Milestone v0.1: cấu hình rõ ràng, health check thực sự gọi model, giới hạn timeout và báo nguyên nhân lỗi.
- [ ] Milestone v0.1: `/reset`, `/stats`, `/info`, `/exit`, thống kê token và hủy stream mà không tự xóa lịch sử.
- [ ] Milestone v0.1: test độc lập với LLM thật, hướng dẫn chạy và bằng chứng xác minh.

Các khả năng của giai đoạn 2–6 được bảo toàn trong `reference/MILESTONE-OVERVIEW.md` và phần Future Milestone Requirements của `REQUIREMENTS.md`. Chúng chưa thuộc phạm vi thực thi v0.1.

### Out of Scope

- SQL tùy ý do LLM sinh và thực thi trực tiếp trên ERP — bỏ qua quyền và logic nghiệp vụ.
- Nhúng agent vào ứng dụng .NET Framework cũ hoặc nâng cấp toàn bộ ERP — dùng dịch vụ riêng và adapter/API mỏng.
- Tài khoản ERP toàn quyền dùng chung cho mọi người chat — tool phải kế thừa quyền người đang hỏi.
- Trong v0.1: tool ERP thật, MCP, giao diện web, lưu lịch sử DB, RAG, thao tác ghi, OpenTelemetry và nhiều provider — thuộc milestone sau hoặc tùy chọn, không bị loại khỏi định hướng tổng thể.
- Multi-agent theo phân hệ từ đầu — chỉ cân nhắc khi một agent có quá nhiều tool và đo được lỗi chọn tool.

## Context

### Thứ tự nguồn và phạm vi

1. Người dùng đính chính ngày 2026-10-21: `tutorial/roadmap/Lo-trinh-MAF-cho-chatbot-ERP.pdf` là nguồn lộ trình TOÀN DỰ ÁN.
2. `docs/superpowers/specs/2026-10-20-day01-maf-erp-chatbot-design.md` là design của milestone ĐẦU TIÊN, với review D1–D10 đã chốt.
3. `docs/superpowers/plans/2026-10-21-day01-maf-erp-chatbot.md` là implementation plan cũ cho cùng milestone đầu tiên; chuyển các ý định sang GSD, không coi checkbox chưa tick là bằng chứng code chưa có.
4. Workspace hiện tại là nguồn sự thật về tiến độ code. Danh mục nguồn và cách xử lý chênh lệch nằm ở `reference/SOURCES.md`.

### Baseline đang có

- `tutorial/ngay-01/Ex1`–`Ex4` minh họa từ `IChatClient` đến MAF; đây là ví dụ học, không phải sản phẩm hoàn tất.
- Commit `bd1051b`: bootstrap `code/ErpChatbot.sln`, Core models/interfaces, test Core.
- Commit `ad09bbd`: Infrastructure Ollama factory/health check và test tương ứng.
- Tại lúc khảo sát đầu, Agent project/test là WIP; trong lúc khởi tạo đã xuất hiện commit `8dc0574` cho Agent service/streaming. Một phiên khác có thể tiếp tục sửa code; cần refresh khi lập plan.
- Chưa xác minh build/test hoặc chat với Ollama trong phiên khởi tạo này. Không chạy build toàn solution trong lúc WIP đang thay đổi để tránh ghi nhận kết quả sai thời điểm.
- Xem `codebase/ARCHITECTURE.md` và `codebase/STACK.md` cho baseline giới hạn đã khảo sát; chưa chạy full `$gsd-map-codebase`.

### Các cổng sau milestone đầu tiên

- Trước giai đoạn 2: biết phiên bản .NET Framework, API Web API/WCF hiện có, cơ chế đăng nhập/đổi token, chính sách gửi dữ liệu ra cloud và 10 câu hỏi nghiệp vụ ưu tiên.
- Pilot sau giai đoạn 4: 3–5 người dùng thật trên môi trường test, chỉ đọc, phân quyền theo người dùng, phiên hội thoại riêng và lưu được để mở lại.
- Trước rollout rộng: bộ 50–100 câu có đáp án, ngưỡng đúng đề xuất >=90%, không lộ dữ liệu ngoài quyền, không bịa khi thiếu dữ liệu, trace/audit đầy đủ, quota/cảnh báo chi phí và phản hồi đúng/sai. Đây là cổng tương lai, không phải kết quả hiện tại.

## Constraints

- **Runtime**: .NET 10 cho code mới; giữ ERP .NET Framework riêng. PDF cho phép .NET 8/10, design v0.1 đã chọn .NET 10.
- **Provider v0.1**: Ollama duy nhất, cấu hình `LLM_PROVIDER`, `LLM_MODEL`, `OLLAMA_ENDPOINT`; provider khác phải bị từ chối rõ ràng. Provider cloud chỉ quyết định sau khi rõ chính sách dữ liệu.
- **Architecture**: Core, Infrastructure, Agent, CLI và Tests; Agent không phụ thuộc Infrastructure. Composition root ở CLI.
- **Core abstraction**: design ghi Core không có thư viện AI bên thứ ba nhưng `IChatClientFactory` trả `IChatClient`. Baseline thực tế dùng `Microsoft.Extensions.AI.Abstractions`; giữ seam abstraction và ghi nhận ngoại lệ này, không coi Core hoàn toàn không có dependency AI.
- **Conversation ownership**: một service instance cho một conversation; web tương lai phải kiểm tra quyền sở hữu phiên và tránh singleton chứa lịch sử dùng chung.
- **Health**: request thật xác nhận model, timeout 5 giây, phân biệt mất kết nối/thiếu model/timeout.
- **Streaming**: token lấy qua `UsageContent` trong `AgentResponseUpdate.Contents`; Ctrl+C và lỗi giữa stream không tự reset session. `/reset` giữ thống kê của phiên CLI.
- **Versions**: phiên bản trong tài liệu và `.csproj` là baseline được pin, chưa phải khẳng định API mới nhất; xác minh qua Context7 trước khi dùng API hoặc sửa cấu hình SDK.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| PDF là nguồn lộ trình tổng thể, design/plan Ngày 1 chỉ cho milestone đầu | Người dùng đã đính chính phạm vi | Confirmed by user |
| v0.1 bao phủ nền tảng và agent console, tương ứng giai đoạn 0–1 | Kế thừa hai tài liệu Ngày 1 đã có; không đánh đồng ngày học với giai đoạn PDF | Proposed; roadmap awaiting approval |
| Các milestone sau giữ ở định hướng; chỉ chi tiết hóa khi bắt đầu | Chưa rõ API ERP, auth, chính sách dữ liệu và câu hỏi nghiệp vụ | Proposed |
| Ollama-only, 4 project + Tests, Agent độc lập Infrastructure | Quyết định D1–D10 trong design đã review | Accepted baseline; implementation unverified |
| Source review thay cho nghiên cứu ecosystem mới ở bước khởi tạo | Phạm vi và nguồn đã có; tránh tự thay đổi quyết định đã chốt | Initial research skipped; phase research enabled |
| Structured output ghi ở giai đoạn 1 của PDF nhưng chưa có trong design v0.1 | Mốc Ngày 1 đã chốt trả text; output bảng/DTO cần khi có tool dữ liệu | Explicit deferral to v0.2, review before milestone scope lock |

## Evolution

Sau mỗi phase: cập nhật yêu cầu đã xác minh, các quyết định mới, blocker và mô tả sản phẩm nếu thực tế thay đổi. Sau mỗi milestone: rà lại core value, phạm vi loại trừ, trạng thái code và cổng nghiệm thu tiếp theo. Không tự đánh dấu yêu cầu hoàn thành chỉ vì file đã tồn tại.

---
*Last updated: 2026-10-21 after initialization from the corrected full-project roadmap source.*
