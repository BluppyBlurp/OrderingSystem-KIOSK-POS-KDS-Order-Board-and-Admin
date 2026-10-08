using Kiosk.Api.Auth;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Orders;
using Kiosk.Domain.Devices;
using Kiosk.Domain.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Kiosk.Api.Hubs;

public static class HubGroups
{
    public const string Kitchen = "kitchen";
    public const string Pos = "pos";
    public const string Board = "board";
    public const string Kiosks = "kiosks";
    public static string KioskOrder(Guid orderId) => $"kiosk-{orderId}";
}

/// <summary>
/// Groups are assigned from the caller's identity on connect; clients cannot join a group they are not entitled to.
/// A kiosk joins kiosk-{orderId} only for orders it created.
/// </summary>
[Authorize(Policy = Policies.AnyClient)]
public sealed class OrdersHub(KioskOrderService kioskOrders) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        var groups = new List<string>();

        if (user.HasClaim(Claims.DeviceKind, nameof(DeviceKind.Kiosk))) groups.Add(HubGroups.Kiosks);
        if (user.HasClaim(Claims.DeviceKind, nameof(DeviceKind.Board))) groups.Add(HubGroups.Board);
        if (user.HasAnyRole(Roles.Admin, Roles.Manager, Roles.Kitchen)) groups.Add(HubGroups.Kitchen);
        if (user.HasAnyRole(Roles.Admin, Roles.Manager, Roles.Cashier)) groups.Add(HubGroups.Pos);
        if (user.HasAnyRole(Roles.Admin, Roles.Manager, Roles.Cashier, Roles.Kitchen)) groups.Add(HubGroups.Board);

        foreach (var group in groups.Distinct())
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await base.OnConnectedAsync();
    }

    /// <summary>Kiosk subscribes to live status for an order it just created (payment confirmation screen).</summary>
    [Authorize(Policy = Policies.Kiosk)]
    public async Task WatchOrder(Guid orderId)
    {
        if (!await kioskOrders.IsOwnedByAsync(orderId, Context.User!.GetDeviceId(), Context.ConnectionAborted))
            throw new HubException("Order not found.");
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.KioskOrder(orderId));
    }
}

/// <summary>Routes each status change to the screens that care, under the event names in docs §9.</summary>
internal sealed class SignalROrderNotifier(IHubContext<OrdersHub> hub, ILogger<SignalROrderNotifier> logger) : IOrderNotifier
{
    public async Task OrderChangedAsync(OrderDto order, CancellationToken ct = default)
    {
        var eventName = $"Order{order.Status}";
        var staffGroups = order.Status switch
        {
            OrderStatus.AwaitingPayment or OrderStatus.Expired or OrderStatus.Cancelled => [HubGroups.Pos],
            OrderStatus.Paid => [HubGroups.Kitchen, HubGroups.Pos],
            OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Completed => new[] { HubGroups.Kitchen },
            _ => [],
        };

        try
        {
            await hub.Clients.Group(HubGroups.KioskOrder(order.Id)).SendAsync(eventName, order, ct);
            if (staffGroups.Length > 0)
                await hub.Clients.Groups(staffGroups).SendAsync(eventName, order, ct);

            // The public board gets only what it displays, never items or totals.
            if (order.Status is OrderStatus.Paid or OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Completed)
                await hub.Clients.Group(HubGroups.Board).SendAsync(eventName,
                    new { order.OrderNumber, order.DiningOption, order.Type, order.TableNumber, order.Status }, ct);
        }
        catch (Exception ex)
        {
            // The change is already committed; clients recover by refetching on reconnect.
            logger.LogWarning(ex, "Realtime push failed for order {Order}", order.OrderNumber);
        }
    }

    public async Task MenuChangedAsync(CancellationToken ct = default)
    {
        try { await hub.Clients.Group(HubGroups.Kiosks).SendAsync("MenuChanged", ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime MenuChanged push failed"); }
    }
}
