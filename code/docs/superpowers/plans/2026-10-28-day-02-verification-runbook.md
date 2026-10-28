# Day 2 Verification Runbook: Function Calling, RBAC & Human-in-the-Loop

- **Ngày thực hiện**: 2026-10-28
- **Phiên bản**: .NET 10 / Microsoft.Agents.AI 1.25.0 / Microsoft.Extensions.AI 10.11.0
- **Trạng thái**: Đã xác thực thành công 64/64 unit tests và build Release thành công sau khi hoàn tất toàn bộ feedback review.

---

## 1. Kết quả Kiểm thử Tự động (Unit Tests)

Chạy lệnh:
```bash
dotnet test code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj
```

Kết quả:
- **Tổng số tests**: 64
- **Passed**: 64
- **Failed**: 0
- **Thời gian**: ~0.35s

### Bảng phân bổ test theo từng phân hệ:
| Phân hệ | Số lượng test | Nội dung kiểm thử |
|---|---|---|
| **Core & RBAC** | 7 tests | - Quyền `Sales` (có Tồn kho, Đơn hàng, Đề xuất; không có Công nợ).<br/>- Quyền `Accountant` (có Công nợ, Tồn kho, Đơn hàng).<br/>- Quyền `Admin` (toàn quyền).<br/>- Hàm `SwitchUser`, ném exception khi user lạ.<br/>- Toàn vẹn DTOs `StockInfo`, `OrderStatusInfo`, `CustomerDebtInfo`, `PurchaseRequestResult` và Event models. |
| **Infrastructure ERP** | 16 tests | - `InMemoryErpDataService` tra tồn kho `SP-001`, `SP-002`, lỗi `SP-999`.<br/>- Tra đơn hàng `DH-1001`, đơn hàng không tồn tại.<br/>- Tra công nợ `KH-001` (45M), `KH-002` (120.5M).<br/>- Tạo phiếu `PR-xxxx` thành công.<br/>- **Validate đầu vào**: Chặn `quantity <= 0` (ném `ArgumentOutOfRangeException`).<br/>- `ChatClientFactory` health check (Ready, Missing, Timeout, Unreachable) và provider validation. |
| **Agent Tools & RBAC** | 26 tests | - Khởi tạo 5 tools (`get_stock`, `get_order_status`, `list_low_stock`, `get_customer_debt`, `create_purchase_request`).<br/>- Tool `create_purchase_request` bọc bằng `ApprovalRequiredAIFunction`.<br/>- **Tool Filtering**: Lọc tools theo quyền của từng user (Sales không thấy debt, Accountant không thấy create PR, Admin thấy tất cả).<br/>- **Fail-Secure (Default Deny)**: Tool chưa đăng ký bị từ chối trả structured error `ToolSecurityError("PermissionDenied", ...)`.<br/>- **Structured Error**: `sales_user` tra công nợ nhận structured error giải thích rõ quyền hạn thay vì crash exception.<br/>- **Conditional Approval & Input Validation**: `quantity <= 0` không được auto-duyệt; `quantity > 0 && <= 50` tự duyệt; `> 50` yêu cầu duyệt.<br/>- **Cumulative Turn Threshold**: Cộng dồn trong lượt (30 + 25 = 55 > 50) kích hoạt yêu cầu duyệt người dùng.<br/>- **Pre-Approval RBAC Check**: User thiếu quyền `PurchaseRequest:Create` bị từ chối ngay, không bao giờ phát sinh event hỏi duyệt.<br/>- Streaming Event (`AgentTextChunkEvent`, `AgentToolExecutionEvent`, `AgentTurnCompletedEvent`).<br/>- `ResetSessionAsync` giữ nguyên `SessionStats`. |
| **CLI & Commands** | 15 tests | - Lệnh `/user <username>` chuyển đổi user và **tự động gọi ResetSessionAsync**.<br/>- Lệnh `/whoami` hiển thị thông tin tài khoản, vai trò và quyền hạn.<br/>- Lệnh `/reset`, `/stats`, `/info`, `/exit`. |

---

## 2. Kịch bản Xác thực Thủ công trên CLI (Manual Smoke Test Scenarios)

Khởi động ứng dụng:
```bash
dotnet run --project code/src/ErpChatbot.Cli
```

### Kịch bản 1: Kiểm tra vai trò mặc định và tra cứu tồn kho
1. Khởi động ứng dụng -> Banner hiển thị user mặc định: `sales_user (Sales)`.
2. Gõ `/whoami` -> Hiển thị:
   - User: `sales_user` (Nguyễn Văn Bán)
   - Roles: `Sales`
   - Permissions: `Inventory:Read`, `Orders:Read`, `PurchaseRequest:Create`
3. Hỏi: `Mã SP-002 còn bao nhiêu trong kho?`
   - Mô hình gọi tool: `[tool] get_stock(productCode=SP-002) -> ok`
   - Trả lời: *"Sản phẩm Chuột không dây M3 (SP-002) hiện còn 8 cái tại Kho Hà Nội."*

### Kịch bản 2: Kiểm tra phân quyền RBAC & Lỗi có cấu trúc (Chặn sales tra nợ)
1. Đang là `sales_user`, hỏi: `Công ty Ánh Dương KH-001 đang nợ bao nhiêu?`
   - Hệ thống không cung cấp tool `get_customer_debt` cho `sales_user` (hoặc nếu gọi, `RbacFunctionInvoker` trả về structured error `PermissionDenied`).
   - Trợ lý giải thích lịch sự, rõ ràng: *"Bạn không có quyền tra cứu thông tin công nợ khách hàng (cần quyền Debt:Read)."* (Không xuất hiện exception hay lỗi ngắt kết nối).

### Kịch bản 3: Chuyển tài khoản & Cô lập phiên hội thoại (/user)
1. Gõ: `/user accountant_user`
   - Hệ thống chuyển sang: `accountant_user` (Trần Thị Kế Toán - vai trò Accountant).
   - Hệ thống in thông báo: `(Đã tự động làm mới phiên hội thoại để bảo mật dữ liệu giữa các người dùng)`.
2. Hỏi: `Công ty Ánh Dương KH-001 đang nợ bao nhiêu?`
   - Mô hình gọi tool: `[tool] get_customer_debt(customerCode=KH-001) -> ok`
   - Trợ lý trả lời số liệu công nợ thật: 45.000.000 VNĐ.
3. Gõ tiếp: `/user sales_user`
4. Hỏi: `Tóm tắt lại công nợ khách hàng mà tôi vừa hỏi lúc nãy?`
   - Trợ lý trả lời rằng phiên làm việc mới không có thông tin công nợ nào trước đó, bảo mật hoàn toàn dữ liệu giữa các tài khoản!

### Kịch bản 4: Phê duyệt đề xuất mua hàng tự động (số lượng <= 50)
1. Đang là `sales_user` (hoặc `admin_user`), hỏi: `Tạo đề xuất mua 20 cái SP-003`
   - Tool ghi `create_purchase_request` được gọi với `quantity = 20`.
   - Hệ thống nhận thấy `quantity <= 50` và người dùng có quyền `PurchaseRequest:Create` -> Tự động duyệt.
   - Console hiển thị: `[tool] create_purchase_request(...) -> ok`.
   - Trợ lý thông báo đã tạo thành công phiếu `PR-xxxx`.

### Kịch bản 5: Phê duyệt đề xuất mua hàng có tương tác (số lượng > 50)
1. Hỏi: `Tạo đề xuất đặt mua 100 cái SP-003`
   - Tool ghi `create_purchase_request` với `quantity = 100`.
   - Hệ thống tạm dừng stream, in hộp thoại màu vàng:
     ```
     [cần duyệt] create_purchase_request(productCode=SP-003, quantity=100). Đồng ý? (y/n): 
     ```
   - Nhập `y`: Hệ thống ghi vào ERP -> Trợ lý báo đã tạo thành công phiếu `PR-xxxx`.
   - Nếu thử lại và nhập `n`: Trợ lý báo đề xuất đã bị từ chối và không ghi vào ERP.

### Kịch bản 6: Kiểm tra quyền trước khi xin duyệt (RBAC Pre-Approval)
1. Gõ: `/user accountant_user` (không có quyền `PurchaseRequest:Create`).
2. Hỏi: `Tạo đề xuất mua 100 cái màn hình SP-003`
   - Hệ thống từ chối ngay lập tức, **tuyệt đối không hiển thị câu hỏi `Đồng ý? (y/n)`**.
   - Trợ lý thông báo: Tài khoản của bạn không có quyền tạo đề xuất mua hàng.
