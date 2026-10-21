# Nguồn và nguyên tắc chuyển sang GSD

## Source register

| Nguồn | Phạm vi | Cách dùng |
|-------|---------|----------|
| `tutorial/roadmap/Lo-trinh-MAF-cho-chatbot-ERP.pdf` | Toàn dự án, 9 trang, 7 giai đoạn 0–6 | Vision, thứ tự milestone, pilot, bảo mật và cổng dùng thật |
| `docs/superpowers/specs/2026-10-20-day01-maf-erp-chatbot-design.md` | Milestone v0.1 | Quyết định D1–D10, Ollama, CLI, session và test |
| `docs/superpowers/plans/2026-10-21-day01-maf-erp-chatbot.md` | Milestone v0.1 | Task 1–5 để chuyển sang plan GSD sau khi khảo sát code |
| Workspace `code/`, `git log`, `git status` | Trạng thái thực thi hiện tại | Ưu tiên hơn mô tả lịch sử "code trống" hoặc checkbox cũ |

PDF SHA-256: `423ef476726bfabaae8594af3aa097ec0886524edef0423b25b8b20dca7e039f`.
PDF đang nằm trong thư mục chưa track tại lúc khởi tạo; lượt này không tự thêm PDF hoặc tài liệu tham khảo khác vào Git. Các tóm tắt GSD giữ các ý định quan trọng, nhưng máy khác cần có PDF gốc nếu muốn đối chiếu toàn văn.

## Evidence by PDF page

- Trang 1–3: dịch vụ trợ lý tách ERP; tool gọi API, không cho AI truy vấn DB tùy ý; roadmap 7 giai đoạn; vector store tùy chọn.
- Trang 4: giai đoạn 0 nền tảng, giai đoạn 1 MAF/session, structured output; bắt đầu giai đoạn 2 với read tool.
- Trang 5: 5–10 câu hỏi/tool, DTO nhỏ/phân trang, 10 câu đối chiếu ERP; tích hợp legacy/MCP (MCP không bắt buộc); UI chat có auth và lịch sử riêng; pilot 3–5 người.
- Trang 6: guardrails, OTel, evaluation 50–100 câu, RAG tùy chọn; write approval/workflow và multi-agent có điều kiện.
- Trang 6–7: API mỏng legacy ưu tiên, DB read-only là phương án dự phòng có kiểm soát; không nhúng agent vào legacy; auth theo người đang chat và tool theo vai trò.
- Trang 7–8: giảm dữ liệu nhạy cảm, audit, prompt injection, quota, feedback và cổng rollout; câu hỏi mở về auth/API/chính sách AI/10 câu hỏi ưu tiên.
- Trang 8–9: link nguồn và lưu ý code C# minh họa chưa biên dịch. Không biến ví dụ hoặc tuyên bố phiên bản trong PDF thành xác minh API hiện tại.

## Reconciliation

1. "Ngày 1" trong tutorial/design không tương đương một ngày lịch hoặc một giai đoạn duy nhất của PDF. v0.1 gộp giai đoạn 0–1 ở mức console.
2. Ollama-only là giới hạn v0.1. PDF cho phép cloud provider về sau; không khóa cả dự án vào Ollama.
3. MCP, RAG, thao tác ghi và multi-agent có tính tùy chọn/điều kiện; ghi rõ trước khi lập milestone tương ứng.
4. Structured output ở PDF giai đoạn 1 được dời rõ sang milestone tool/DTO; không lặng lẽ bỏ khỏi lộ trình hoặc thêm vào v0.1 đã chốt.
5. Mermaid cũ có cạnh Agent→Infrastructure nhưng bảng design D6 và implementation plan cấm cạnh này: dùng quyết định D6; không sửa nguồn trong lượt chuyển đổi.
6. Core thực tế tham chiếu AI.Abstractions: không khẳng định nó hoàn toàn độc lập mọi thư viện AI. Tách provider/runtime khỏi abstraction.
7. Task/checkbox trong plan cũ chưa phản ánh các commit mới; kiểm tra lại từng task trước khi tạo GSD `PLAN.md`. Không tự tạo SUMMARY/VERIFICATION hồi tố khi chưa có bằng chứng.
8. Lượt này không thực hiện nghiên cứu bên ngoài hay xác minh API. `workflow.research` bật cho các phase sau; dùng `ctx7` đúng hướng dẫn dự án khi cần API cụ thể.
