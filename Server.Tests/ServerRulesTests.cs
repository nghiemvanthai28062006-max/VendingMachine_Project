using Server.Models;
using Server.Services;
using Xunit;

namespace Server.Tests;

public class OrderRulesTests
{
    [Fact]
    public void CheckCanCreate_Ok_ReturnsNull() =>
        Assert.Null(OrderRules.CheckCanCreate("ACTIVE", true, quantity: 5, holds: 0, jamCount: 0));

    [Fact]
    public void CheckCanCreate_MachineInMaintenance() =>
        Assert.Equal("MACHINE_NOT_ACTIVE", OrderRules.CheckCanCreate("MAINTENANCE", true, 5, 0, 0));

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void CheckCanCreate_ProductInactiveOrMissing(bool? productActive) =>
        Assert.Equal("PRODUCT_INACTIVE", OrderRules.CheckCanCreate("ACTIVE", productActive, 5, 0, 0));

    [Fact]
    public void CheckCanCreate_JamBlocksEvenWhenStockIsEmpty() =>
        Assert.Equal("TRAY_BLOCKED", OrderRules.CheckCanCreate("ACTIVE", true, 0, 0, 1));

    [Fact]
    public void CheckCanCreate_LastBottleAlreadyHeld_IsEmpty() =>
        Assert.Equal("TRAY_EMPTY", OrderRules.CheckCanCreate("ACTIVE", true, quantity: 1, holds: 1, jamCount: 0));

    [Fact]
    public void CheckCanCreate_LastBottleNotHeld_IsOk() =>
        Assert.Null(OrderRules.CheckCanCreate("ACTIVE", true, quantity: 1, holds: 0, jamCount: 0));

    [Fact]
    public void ExpiresAt_Is120SecondsAfterCreation()
    {
        var created = new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.FromHours(7));
        Assert.Equal(created.AddSeconds(120), OrderRules.ExpiresAtFor(created));
    }

    [Fact]
    public void IsPaymentOverdue_BoundaryAndStatus()
    {
        var expires = new DateTimeOffset(2026, 11, 22, 9, 2, 0, TimeSpan.FromHours(7));
        Assert.False(OrderRules.IsPaymentOverdue("PENDING_PAYMENT", expires, expires.AddSeconds(-1)));
        Assert.True(OrderRules.IsPaymentOverdue("PENDING_PAYMENT", expires, expires));
        Assert.False(OrderRules.IsPaymentOverdue("PAID", expires, expires.AddHours(1)));
    }

    [Theory]
    [InlineData("PAID", "DEVICE_NO_ACK")]
    [InlineData("DISPENSING", "DISPENSE_TIMEOUT")]
    [InlineData("PENDING_PAYMENT", null)]
    [InlineData("COMPLETED", null)]
    public void StaleRefundReason_ByStatus(string status, string? expected) =>
        Assert.Equal(expected, OrderRules.StaleRefundReason(status));

    [Theory]
    [InlineData("PENDING_PAYMENT", "PAID", true)]
    [InlineData("PENDING_PAYMENT", "EXPIRED", true)]
    [InlineData("PAID", "DISPENSING", true)]
    [InlineData("DISPENSING", "COMPLETED", true)]
    [InlineData("DISPENSING", "DISPENSE_FAILED", true)]
    [InlineData("DISPENSE_FAILED", "REFUNDED", true)]
    [InlineData("PENDING_PAYMENT", "COMPLETED", false)]
    [InlineData("PAID", "COMPLETED", false)]
    [InlineData("COMPLETED", "REFUNDED", false)]
    [InlineData("EXPIRED", "PAID", false)]
    [InlineData("REFUNDED", "COMPLETED", false)]
    public void CanTransition_FollowsDocumentedTable(string from, string to, bool expected) =>
        Assert.Equal(expected, OrderRules.CanTransition(from, to));

    [Fact]
    public void OrderStatus_HasEightStates() => Assert.Equal(8, OrderStatus.All.Count);
}

public class AlertAndTrayRulesTests
{
    [Theory]
    [InlineData(0, 2, "OUT_OF_STOCK")]
    [InlineData(1, 2, "LOW_STOCK")]
    [InlineData(2, 2, "LOW_STOCK")]
    [InlineData(3, 2, null)]
    public void StockAlertFor(int quantityAfter, int threshold, string? expected) =>
        Assert.Equal(expected, AlertRules.StockAlertFor(quantityAfter, threshold));

    [Fact]
    public void TrayAvailable_WhenAllConditionsMet() =>
        Assert.True(TrayRules.IsAvailable("ACTIVE", true, quantity: 3, holds: 1, jamCount: 0));

    [Theory]
    [InlineData("MAINTENANCE", true, 3, 0, 0)]
    [InlineData("ACTIVE", false, 3, 0, 0)]
    [InlineData("ACTIVE", true, 3, 3, 0)]
    [InlineData("ACTIVE", true, 3, 0, 1)]
    [InlineData("ACTIVE", true, 0, 0, 0)]
    public void TrayNotAvailable(string machineStatus, bool productActive, int quantity, int holds, int jam) =>
        Assert.False(TrayRules.IsAvailable(machineStatus, productActive, quantity, holds, jam));
}

public class MachineRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.FromHours(7));

    [Fact] public void NullHeartbeat_IsOffline() => Assert.Equal("OFFLINE", MachineRules.ConnectionOf(null, Now));
    [Fact] public void Heartbeat30sAgo_IsOnline() => Assert.Equal("ONLINE", MachineRules.ConnectionOf(Now.AddSeconds(-30), Now));
    [Fact] public void Heartbeat60sAgo_IsStillOnline() => Assert.Equal("ONLINE", MachineRules.ConnectionOf(Now.AddSeconds(-60), Now));
    [Fact] public void Heartbeat61sAgo_IsOffline() => Assert.Equal("OFFLINE", MachineRules.ConnectionOf(Now.AddSeconds(-61), Now));
}

public class RestockRulesTests
{
    [Theory]
    [InlineData(true, 0, 10, 2, true)]
    [InlineData(true, 2, 10, 2, true)]
    [InlineData(true, 3, 10, 2, false)]
    [InlineData(false, 0, 10, 2, false)]
    [InlineData(true, 5, 5, 8, false)]   // đã đầy thì không còn chỗ châm
    public void NeedsRestock(bool hasProduct, int quantity, int capacity, int threshold, bool expected) =>
        Assert.Equal(expected, RestockRules.NeedsRestock(hasProduct, quantity, capacity, threshold));

    [Fact] public void PlannedQty_IsCapacityMinusQuantity() => Assert.Equal(9, RestockRules.PlannedQty(10, 1));

    [Theory]
    [InlineData(1, 9, 10, false)]
    [InlineData(1, 10, 10, true)]
    [InlineData(0, 10, 10, false)]
    public void WouldExceedCapacity(int quantity, int add, int capacity, bool expected) =>
        Assert.Equal(expected, RestockRules.WouldExceedCapacity(quantity, add, capacity));

    [Theory]
    [InlineData("OPEN", true, true, true)]
    [InlineData("IN_PROGRESS", false, true, true)]
    [InlineData("DONE", false, false, false)]
    [InlineData("CANCELLED", false, false, false)]
    public void TicketTransitions(string status, bool canStart, bool canConfirm, bool canCancel)
    {
        Assert.Equal(canStart, RestockRules.CanStart(status));
        Assert.Equal(canConfirm, RestockRules.CanConfirm(status));
        Assert.Equal(canCancel, RestockRules.CanCancel(status));
    }

    [Fact]
    public void BuildSuggestions_SortsByUrgencyAndCounts()
    {
        var rows = new List<RestockCandidate>
        {
            new() { MachineId = 2, MachineCode = "M02", TrayId = 9, TrayCode = "A3", Quantity = 2, Capacity = 8 },
            new() { MachineId = 1, MachineCode = "M01", TrayId = 4, TrayCode = "B1", Quantity = 0, Capacity = 10 },
            new() { MachineId = 1, MachineCode = "M01", TrayId = 3, TrayCode = "A3", Quantity = 1, Capacity = 10 }
        };

        var result = RestockRules.BuildSuggestions(rows);

        Assert.Equal(2, result.Count);
        Assert.Equal("M01", result[0].MachineCode);            // có khay hết hàng nên đứng đầu
        Assert.Equal(1, result[0].OutOfStockTrays);
        Assert.Equal(1, result[0].LowStockTrays);
        Assert.Equal(new[] { "A3", "B1" }, result[0].Trays.Select(t => t.TrayCode));
        Assert.Equal(new[] { 9, 10 }, result[0].Trays.Select(t => t.PlannedQty));
        Assert.Equal(6, result[1].Trays[0].PlannedQty);
    }

    [Fact]
    public void BuildSuggestions_SameUrgency_OrdersByMachineId()
    {
        var rows = new List<RestockCandidate>
        {
            new() { MachineId = 5, MachineCode = "M05", TrayId = 1, TrayCode = "A1", Quantity = 1, Capacity = 10 },
            new() { MachineId = 3, MachineCode = "M03", TrayId = 2, TrayCode = "A1", Quantity = 1, Capacity = 10 }
        };
        Assert.Equal(new[] { 3, 5 }, RestockRules.BuildSuggestions(rows).Select(s => s.MachineId));
    }
}