# Requirements: MAF-Chatbot ERP

**Defined:** 2026-10-21
**Core Value:** Trả lời đúng từ ERP trong quyền người hỏi; thiếu dữ liệu thì nói rõ, không bịa số.
**Current milestone:** v0.1 — Nền tảng MAF console, tương ứng giai đoạn 0–1 của PDF.

## v1 Requirements

Trong file GSD này, `v1 Requirements` là phạm vi milestone đang hoạt động (v0.1), không phải toàn bộ lộ trình 7 giai đoạn. Tất cả checkbox còn mở đến khi có bằng chứng implementation, kiểm thử và commit; file tồn tại hoặc review design đã clear không thay thế nghiệm thu.

### Kiến trúc và cấu hình

- [ ] **ARCH-01**: Nhà phát triển có thể build solution .NET 10 với Core, Infrastructure, Agent, CLI và Tests; Agent chỉ tham chiếu Core trong các project nội bộ và nhận chat client qua DI.
- [ ] **CONF-01**: Người chạy cấu hình model/endpoint bằng `appsettings.json` và ghi đè bằng `LLM_PROVIDER`, `LLM_MODEL`, `OLLAMA_ENDPOINT`.
- [ ] **CONF-02**: Người chạy nhận lỗi rõ ràng khi chọn provider ngoài `ollama`, không âm thầm dùng provider khác.
- [ ] **CONF-03**: Người chạy có thể đặt temperature và giới hạn output token; giá trị đó thực sự được truyền vào lượt gọi mô hình.

### Kết nối và hội thoại

- [ ] **HEALTH-01**: Khi khởi động, ứng dụng gọi một request ngắn tới model thật và dừng probe sau tối đa 5 giây nếu model không phản hồi.
- [ ] **HEALTH-02**: Người chạy nhận hướng dẫn khác nhau cho thiếu model, không kết nối được endpoint và timeout.
- [ ] **CHAT-01**: Người dùng nhận câu trả lời tiếng Việt theo persona/instructions ERP đã cấu hình.
- [ ] **CHAT-02**: Người dùng nhìn thấy phản hồi xuất hiện từng phần qua MAF streaming trước khi lượt trả lời kết thúc.
- [ ] **CHAT-03**: Người dùng có thể hỏi tiếp và agent nhớ nội dung lượt trước trong cùng conversation nhờ `AgentSession`.
- [ ] **CHAT-04**: Khi hỏi số liệu ERP trong v0.1, trợ lý nói rõ chưa được kết nối dữ liệu thay vì tự tạo số liệu.
- [ ] **SESS-01**: Mỗi service instance giữ conversation riêng; contract mô tả rõ quyền sở hữu state và cấm singleton chia sẻ lịch sử cho nhiều user ở web tương lai.

### Lệnh và thống kê

- [ ] **SESS-02**: Người dùng gõ `/reset` để xóa ngữ cảnh và bắt đầu session mới, trong khi thống kê cả phiên CLI vẫn giữ nguyên.
- [ ] **STAT-01**: Thống kê cộng dồn đúng token qua `UsageContent` của stream MAF và số lượt qua nhiều lần hỏi.
- [ ] **STAT-02**: Người dùng gõ `/stats` để xem số lượt, input token, output token và tổng token tích lũy.
- [ ] **CLI-01**: Người chạy thấy banner/thông số kết nối lúc mở app và dùng `/info` để xem cấu hình, instructions và danh sách lệnh.
- [ ] **CLI-02**: Người dùng gõ `/exit` hoặc kết thúc input để thoát ứng dụng an toàn.
- [ ] **CLI-03**: Input rỗng bị bỏ qua; lệnh `/xxx` không hợp lệ hiện hướng dẫn và không gửi request LLM.

### Hủy, lỗi và phục hồi

- [ ] **RES-01**: Người dùng nhấn Ctrl+C khi streaming để hủy cả request HTTP đang chờ qua cancellation token và thấy thông báo hủy tách khỏi text.
- [ ] **RES-02**: Nếu mất kết nối/timeout giữa stream, người dùng thấy lỗi trên dòng riêng, phần text đã nhận vẫn rõ ràng và ứng dụng không crash.
- [ ] **RES-03**: Sau lỗi hoặc hủy giữa stream, session không tự bị reset; người dùng có thể hỏi tiếp hoặc tự chọn `/reset`.

### Verification và khả năng chạy lại

- [ ] **TEST-01**: Bộ unit/integration test với fake `IChatClient` kiểm chứng factory, model missing/timeout/network, stream, history, reset, token, cancellation và các lệnh mà không cần Ollama chạy thật.
- [ ] **TEST-02**: Test kiểm chứng hai conversation không lẫn history/stats, cũng như lịch sử và thống kê không bị reset ngoài ý muốn sau lỗi/hủy.
- [ ] **VERIFY-01**: Có bằng chứng smoke chat nhiều lượt và streaming với Ollama thật; nếu môi trường chưa có model thì ghi rõ manual verification còn mở, không tuyên bố milestone đã pass.
- [ ] **DOC-01**: Nhà phát triển có hướng dẫn cấu hình, build/test/run và báo cáo xác minh thực tế với package version được pin; khác biệt so với design/plan cũ được ghi rõ.

## Future Milestone Requirements

Các mục này được bảo toàn từ PDF, chưa phải cam kết thực thi v0.1. Scope và tiêu chí chi tiết được chốt bằng `$gsd-new-milestone` sau khi hoàn tất milestone hiện tại.

### v0.2 — Read-only ERP tools (PDF giai đoạn 2)

- **ERP-01**: Người dùng tra cứu các câu hỏi nghiệp vụ phổ biến qua 5–10 tool có tên/mô tả rõ; tool gọi API ERP.
- **ERP-02**: Kết quả tool là DTO nhỏ, giới hạn dòng/phân trang; hỏi lại khi mã hàng, kho, khách hàng hoặc khoảng thời gian mơ hồ.
- **ERP-03**: 10 câu mẫu trả đúng số liệu đối chiếu màn hình ERP.
- **ERP-04**: Có structured result/DTO khi cần hiển thị bảng; giữ lại ý định structured output ở PDF giai đoạn 1.
- **AUTH-01**: Tool thực thi với quyền người đang hỏi, được allowlist theo vai trò; xác minh flow token bằng ít nhất 1 tool thật từ giai đoạn này.
- **DATA-01**: Chốt chính sách cloud/on-prem và chỉ gửi các trường dữ liệu cần thiết cho mô hình.

### v0.3 — Legacy integration (PDF giai đoạn 3)

- **LEG-01**: Người dùng nhận được câu trả lời từ ít nhất một phân hệ .NET Framework qua API mỏng tái dùng logic/quyền ERP cũ.
- **MCP-01**: Nếu có nhu cầu dùng lại tool, đóng gói bộ tool thành ERP MCP server; nếu không, giữ trong service và ghi quyết định bỏ qua MCP.

### v0.4 — Embedded chat và pilot (PDF giai đoạn 4)

- **WEB-01**: Người đã đăng nhập ERP chat qua khung nhúng/app riêng và nhận stream bằng AG-UI/SSE hoặc endpoint tương đương.
- **AUTH-02**: Endpoint kiểm tra auth ERP và quyền sở hữu conversation; threadId không được dùng thay cho xác thực.
- **HIST-01**: User lưu/mở lại lịch sử trong DB và chỉ truy cập phiên của mình.
- **PILOT-01**: 3–5 người dùng thật dùng bản chỉ đọc trên môi trường test và phản hồi lỗi.

### v0.5 — Quality và rollout (PDF giai đoạn 5 + cổng dùng thật)

- **SAFE-01**: Có guardrails cho câu ngoài phạm vi, che dữ liệu nhạy cảm và kiểm thử prompt injection từ nội dung ERP.
- **OBS-01**: Mỗi câu trả lời có trace, mỗi tool call có audit người gọi/tool/tham số được xử lý phù hợp chính sách dữ liệu.
- **EVAL-01**: Bộ 50–100 câu có đáp án được chạy lại khi đổi prompt/tool/model và đo tỉ lệ đúng; ngưỡng đề xuất >=90% trước rollout.
- **SAFE-02**: Bộ kiểm thử không phát hiện trả dữ liệu ngoài quyền hoặc bịa số khi không có kết quả.
- **COST-01**: Giới hạn số câu hỏi mỗi user/ngày, theo dõi token và cảnh báo chi phí.
- **FEED-01**: Người dùng đánh giá đúng/sai và có vòng lặp xử lý câu sai/tool còn thiếu.
- **RAG-01**: Tùy nhu cầu, trả lời hướng dẫn/quy trình từ kho tài liệu; chưa cam kết vector database cụ thể.

### v0.6 — Optional write workflow (PDF giai đoạn 6)

- **WRITE-01**: Một thao tác ghi chỉ chạy sau xác nhận rõ của người dùng; có log kiểm toán và xử lý từ chối.
- **FLOW-01**: Quy trình ghi nhiều bước dùng workflow/checkpoint khi cần; không tạo write tool không có approval.
- **MULTI-01**: Chỉ cân nhắc nhiều agent khi số tool quá lớn và đánh giá chứng minh agent hiện tại chọn sai.

## Out of Scope

| Feature | Reason |
|---------|--------|
| LLM tự viết và chạy SQL tùy ý trên ERP | Bỏ qua logic và phân quyền API hiện có |
| Nhúng agent vào legacy hoặc nâng cấp toàn bộ ERP | Dùng dịch vụ mới và API/adapter mỏng |
| Tài khoản service toàn quyền dùng chung | Vi phạm quyền dữ liệu người đang hỏi |
| Provider cloud trong v0.1 | Design đã chốt Ollama-only; chờ chính sách dữ liệu cho milestone sau |
| ERP tools, MCP, web, DB history, OTel, RAG, write trong v0.1 | Giữ ở các milestone tương ứng, không mở rộng scope Ngày 1 |
| Auto-reconnect/circuit breaker và pipeline publish CLI trong v0.1 | Design đã defer; CLI local có thể retry thủ công |

## Traceability

Ánh xạ milestone v0.1 trong roadmap draft, chờ duyệt. Future requirements chưa ánh xạ sang phase hoạt động. Pending nghĩa là chưa có bằng chứng nghiệm thu, kể cả khi code tương ứng đã tồn tại.

| Requirement | Phase | Status |
|-------------|-------|--------|
| ARCH-01 | Phase 1 | Pending |
| CONF-01 | Phase 1 | Pending |
| CONF-02 | Phase 1 | Pending |
| CONF-03 | Phase 1 | Pending |
| HEALTH-01 | Phase 1 | Pending |
| HEALTH-02 | Phase 1 | Pending |
| CHAT-01 | Phase 1 | Pending |
| CHAT-02 | Phase 1 | Pending |
| CHAT-03 | Phase 1 | Pending |
| CHAT-04 | Phase 1 | Pending |
| SESS-01 | Phase 1 | Pending |
| SESS-02 | Phase 2 | Pending |
| STAT-01 | Phase 2 | Pending |
| STAT-02 | Phase 2 | Pending |
| CLI-01 | Phase 2 | Pending |
| CLI-02 | Phase 2 | Pending |
| CLI-03 | Phase 2 | Pending |
| RES-01 | Phase 3 | Pending |
| RES-02 | Phase 3 | Pending |
| RES-03 | Phase 3 | Pending |
| TEST-01 | Phase 3 | Pending |
| TEST-02 | Phase 3 | Pending |
| VERIFY-01 | Phase 3 | Pending |
| DOC-01 | Phase 3 | Pending |

**Coverage:** 24/24 yêu cầu v0.1 được ánh xạ đúng một phase; không bỏ sót hoặc trùng lặp.

---
*Last updated: 2026-10-21 after source-based initialization; awaiting roadmap review.*
