using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BarrageOrders;

public sealed class BarrageMap : MapComponent
{
    private List<BarrageOrder> orders = new();
    public BarrageMap(Map map) : base(map) { }

    public BarrageOrder For(Building_Turret gun) => orders.Find(o => o.Guns.Exists(s => s.Gun == gun));

    public void Cancel(IEnumerable<Building_Turret> guns)
    {
        foreach (var gun in guns) For(gun)?.Remove(gun);
        orders.RemoveAll(o => o.Guns.Count == 0);
    }

    public bool Start(BarrageMode mode, List<IntVec3> cells, IEnumerable<Building_Turret> selected)
    {
        var guns = selected.Where(g => g?.Map == map && GunPort.Supported(g)).Distinct()
            .OrderBy(g => g.thingIDNumber).ToList();
        if (cells.Count == 0 || guns.Count == 0) return false;
        var order = new BarrageOrder(map, mode, cells, guns);
        if (order.PlannedRounds == 0 && order.Guns.All(s => s.Expected != null))
        {
            Messages.Message("BO_NoReach".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }
        Cancel(guns);
        foreach (var gun in guns)
            if (gun.ForcedTarget.IsValid) gun.OrderAttack(LocalTargetInfo.Invalid);
        orders.Add(order);
        string summary = order.PlannedRounds == 0 ? "BO_Waiting".Translate().ToString() :
            "BO_Order".Translate(order.PlannedRounds, guns.Count, cells.Count - order.Unreachable, cells.Count).ToString();
        Messages.Message(summary, guns[0], MessageTypeDefOf.TaskCompletion, false);
        return true;
    }

    public override void MapComponentTick()
    {
        for (int i = orders.Count - 1; i >= 0; i--)
            if (orders[i].Tick()) orders.RemoveAt(i);
    }

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref orders, "barrageOrders", LookMode.Deep);
        orders ??= new List<BarrageOrder>();
    }

    public override void FinalizeInit()
    {
        foreach (var order in orders) order.Attach(map);
    }
}
