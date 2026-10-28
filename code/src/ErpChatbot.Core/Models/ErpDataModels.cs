namespace ErpChatbot.Core.Models;

public record StockInfo(
    string ProductCode,
    string ProductName,
    int Quantity,
    string Unit,
    string Location = "Kho Hà Nội");

public record OrderStatusInfo(
    string OrderCode,
    string Customer,
    string Status,
    string Eta);

public record CustomerDebtInfo(
    string CustomerCode,
    string CustomerName,
    decimal DebtAmount,
    string Currency = "VND",
    string DueDate = "");

public record PurchaseRequestResult(
    string RequestId,
    string ProductCode,
    int Quantity,
    string Status);
