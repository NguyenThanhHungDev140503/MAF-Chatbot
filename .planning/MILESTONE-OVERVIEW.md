# Định hướng milestone: MAF-Chatbot ERP

Nguồn: `tutorial/roadmap/Lo-trinh-MAF-cho-chatbot-ERP.pdf`, giai đoạn 0–6. Số milestone/version dưới đây là cách tổ chức GSD đề xuất; không phải nhãn version có sẵn trong PDF.

Chỉ v0.1 có roadmap phase hoạt động trong `ROADMAP.md`. Các hàng tương lai là định hướng để `$gsd-new-milestone` chốt scope khi đến lượt; không được coi đã plan, đã approve hoặc đang thực thi.

| Milestone | Giai đoạn PDF | Đầu ra và cổng nghiệm thu | Điều kiện |
|-----------|---------------|--------------------------|-----------|
| v0.1 — Nền tảng MAF console | 0 + 1 | Console streaming nhiều lượt nhớ ngữ cảnh; persona ERP trung thực khi chưa có dữ liệu; health/config/lệnh/stats/hủy; test fake và smoke có bằng chứng | Đang khởi tạo; dùng design/plan Ngày 1 |
| v0.2 — Tra cứu ERP chỉ đọc | 2 | Chọn 5–10 câu hỏi phổ biến thành tool có mô tả; DTO giới hạn/phân trang; hỏi lại khi mơ hồ; 10 câu đúng so với ERP; structured result khi hiển thị bảng | Chốt API, auth và chính sách dữ liệu; thử token người dùng với 1 tool thật từ sớm |
| v0.3 — Tích hợp ERP legacy | 3 | Ít nhất 1 câu truy xuất đúng dữ liệu phân hệ .NET Framework qua API mỏng; tái dùng tool | MCP là lựa chọn khi cần tái sử dụng, không bắt buộc để pass milestone |
| v0.4 — Chat trong ERP và pilot | 4 | Khung chat stream có đăng nhập; user chỉ mở lịch sử của mình; session lưu DB và mở lại; 3–5 người dùng thật trên test | Chỉ đọc; threadId không thay xác thực; auth ERP phải hoạt động |
| v0.5 — Đo lường và sẵn sàng rollout | 5 | Guardrails/che PII, trace và audit mọi tool; bộ 50–100 câu có đáp án, đo đúng/sai; quota và cảnh báo token; nút feedback | RAG tài liệu là tùy chọn; ngưỡng đúng >=90% là đề xuất cần chốt bằng eval trước rollout |
| v0.6 — Ghi có xác nhận và workflow | 6 | Một thao tác ghi qua duyệt rõ ràng, có audit; workflow nhiều bước/checkpoint nếu phù hợp | Tùy chọn; không gọi write tool trước approval; multi-agent chỉ khi có bằng chứng cần thiết |

## Pilot gate

Kết thúc giai đoạn 4: 3–5 người thật dùng trên test; số liệu từ API ERP và đúng quyền; có thể mở lại lịch sử riêng. Đây là pilot, chưa đồng nghĩa đủ điều kiện mở toàn công ty.

## Rollout gate

- Tỉ lệ trả lời đúng trên bộ eval đạt ngưỡng đã chốt (PDF gợi ý >=90%).
- Không có kết quả lộ dữ liệu ngoài quyền người hỏi.
- Không có dữ liệu thì báo không tìm thấy, không bịa số.
- Trace và audit đầy đủ mọi tool invocation; có quota và cảnh báo chi phí.
- Người dùng có thể đánh giá đúng/sai; rà soát câu sai và tool còn thiếu định kỳ.

## Discovery trước v0.2

1. ERP legacy đang chạy .NET Framework phiên bản nào, đã có Web API/WCF/ASMX gì?
2. ERP dùng Forms Auth/cookie, Identity, AD hay SSO; token exchange/delegation đi theo cơ chế nào?
3. Dữ liệu nào được phép gửi cloud; provider/vùng lưu trữ hoặc yêu cầu on-prem là gì?
4. 10 câu hỏi ưu tiên và đáp án/màn hình ERP dùng đối chiếu là gì?

Chưa có câu trả lời trong nguồn hiện tại. Đây là blocker của tích hợp thật ở milestone sau, không phải lý do chặn khởi tạo planning hoặc console v0.1.
