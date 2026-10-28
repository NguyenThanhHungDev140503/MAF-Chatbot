# Day 2 Verification Runbook: Function Calling, RBAC & Human-in-the-Loop

- **Ngày thực hiện**: 2026-10-28
- **Phiên bản**: .NET 10 / Microsoft.Agents.AI 1.25.0 / Microsoft.Extensions.AI 10.11.0
- **Trạng thái**: Đã xác thực thành công 51/51 unit tests và build Release thành công.

---

## 1. Kết quả Kiểm thử Tự động (Unit Tests)

Chạy lệnh:
```bash
dotnet test code/ErpChatbot.sln -p:ParallelizeTestCollections=false
```

Kết quả:
- **Tổng số tests**: 51
- **Passed**: 51
- **Failed**: 0
- **Thời gian**: ~0.4s

### Bảng phân bổ test theo từng phân hệ:
| Phân hệ | Số lượng test | Nội dung kiểm thử |
|---|---|---|
| **Core & RBAC** | 7 tests | - Quyền `Sales` (có Tồn kho, Đơn hàng, Đề xuất; không có Công nợ).<br/>- Quyền `Accountant` (có Công nợ, Tồn kho, Đơn hàng).<br/>- Quyền `Admin` (toàn quyền).<br/>- Hàm `SwitchUser`, ném exception khi user lạ.<br/>- Toàn vẹn DTOs `StockInfo`, `OrderStatusInfo`, `CustomerDebtInfo`, `PurchaseRequestResult` và Event models. |
| **Infrastructure ERP** | 13 tests | - `InMemoryErpDataService` tra tồn kho `SP-001`, `SP-002`, lỗi `SP-999`.<br/>- Tra đơn hàng `DH-1001`, đơn hàng không tồn tại.<br/>- Tra công nợ `KH-001` (45M), `KH-002` (120.5M).<br/>- Tạo phiếu `PR-xxxx` ngẫu nhiên.<br/>- `ChatClientFactory` health check (Ready, Missing, Timeout, Unreachable) và safe middleware. |
| **Agent Tools & RBAC** | 16 tests | - Khởi tạo 5 tools (`get_stock`, `get_order_status`, `list_low_stock`, `get_customer_debt`, `create_purchase_request`).<br/>- Tool `create_purchase_request` bọc bằng `ApprovalRequiredAIFunction`.<br/>- `RbacFunctionInvoker` chặn `sales_user` tra công nợ (ném `UnauthorizedAccessException`).<br/>- `RbacFunctionInvoker` cho phép `accountant_user` tra công nợ.<br/>- Duyệt có điều kiện: `quantity <= 50` tự duyệt, `> 50` yêu cầu duyệt.<br/>- Streaming Event (`AgentTextChunkEvent`, `AgentTurnCompletedEvent`).<br/>- `ResetSessionAsync` giữ nguyên `SessionStats`. |
| **CLI & Commands** | 15 tests | - Lệnh `/user <username>` chuyển đổi user runtime.<br/>- Lệnh `/whoami` hiển thị thông tin tài khoản, vai trò và quyền hạn.<br/>- Lệnh `/reset`, `/stats`, `/info`, `/exit`.<br/>- Phục hồi sau rớt mạng, hủy giữa stream không mất session. |

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
   - Permissions: `Inventory:Read`, `Orders:Read`, `PurchaseRequestCreate`
3. Hỏi: `Mã SP-002 còn bao nhiêu trong kho?`
   - Mô hình gọi tool: `[tool] get_stock(productCode=SP-002) -> ok`
   - Trả lời: *"Sản phẩm Chuột không dây M3 (SP-002) hiện còn 8 cái tại Kho Hà Nội."*

### Kịch bản 2: Kiểm tra phân quyền RBAC (Chặn sales tra nợ)
1. Đang là `sales_user`, hỏi: `Công ty Ánh Dương KH-001 đang nợ bao nhiêu?`
   - Mô hình gọi tool: `get_customer_debt(customerCode=KH-001)`
   - `RbacFunctionInvoker` chặn và báo lỗi thiếu quyền `Debt:Read`.
   - Trợ lý trả lời lịch sự: *"Tài khoản của bạn (vai trò Sales) không có quyền tra cứu thông tin công nợ khách hàng."*

### Kịch bản 3: Chuyển quyền sang kế toán và tra cứu thành công
1. Gõ: `/user accountant_user`
   - Hệ thống chuyển sang: `accountant_user` (Trần Thị Kế Toán - vai trò Accountant).
2. Hỏi lại: `Công ty Ánh Dương KH-001 đang nợ bao nhiêu?`
   - Mô hình gọi tool: `[tool] get_customer_debt(customerCode=KH-001) -> ok`
   - Trợ lý trả lời: *"Công ty TNHH Ánh Dương (KH-001) hiện đang có công nợ là 45.000.000 VNĐ, hạn thanh toán là 2026-11-15."*

### Kịch bản 4: Phê duyệt đề xuất mua hàng tự động (số lượng <= 50)
1. Hỏi: `Tạo đề xuất mua 20 cái SP-003`
   - Tool ghi `create_purchase_request` được gọi với `quantity = 20`.
   - Hệ thống nhận thấy `quantity <= 50` -> Tự động duyệt.
   - Log console hiển thị: `[tool] create_purchase_request(...) -> Tự động phê duyệt (số lượng <= 50)`.
   - Trợ lý thông báo đã tạo thành công phiếu `PR-xxxx` và đang chờ trưởng phòng mua hàng phê duyệt.

### Kịch bản 5: Phê duyệt đề xuất mua hàng có tương tác (số lượng > 50)
1. Hỏi: `Tạo đề xuất đặt mua 100 cái SP-003`
   - Tool ghi `create_purchase_request` với `quantity = 100`.
   - Hệ thống tạm dừng stream, in hộp thoại màu vàng:
     ```
     [cần duyệt] create_purchase_request(productCode=SP-003, quantity=100). Đồng ý? (y/n): 
     ```
   - Nhập `y`: Hệ thống ghi vào ERP -> Trợ lý báo đã tạo thành công phiếu `PR-xxxx`.
   - Nếu thử lại và nhập `n`: Trợ lý báo đề xuất đã bị người dùng từ chối và không ghi vào ERP.
