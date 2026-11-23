using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BarrageOrders;

public sealed class GunQueue : IExposable
{
    public Building_Turret Gun;
    public IntVec3 Active = IntVec3.Invalid;
    public ThingDef Expected;
    public ThingDef ActiveAmmo;
    public int Sent;
    internal int SeenShot;
    internal bool Fired;
    internal int Wave;
    internal int Priority = int.MaxValue;
    internal readonly Queue<(IntVec3 cell, int wave)> Next = new();
    internal int Burst = 1;
    internal int PendingRounds
    {
        get
        {
            int queued = Next.Count * Burst;
            if (!Active.IsValid) return queued;
            return queued + (Fired ? GunPort.ShotsLeft(Gun.AttackVerb) : Burst);
        }
    }

    public void ExposeData()
    {
        Scribe_References.Look(ref Gun, "turret");
        Scribe_Values.Look(ref Active, "target", IntVec3.Invalid);
        Scribe_Defs.Look(ref Expected, "expectedProjectile");
        Scribe_Defs.Look(ref ActiveAmmo, "issuedProjectile");
        Scribe_Values.Look(ref Sent, "sent");
        Scribe_Values.Look(ref SeenShot, "lastObservedShot", -1);
        Scribe_Values.Look(ref Fired, "firedInBurst");
    }

    internal void Release()
    {
        if (Active.IsValid && Gun?.Spawned == true && Gun.ForcedTarget == new LocalTargetInfo(Active))
            Gun.OrderAttack(LocalTargetInfo.Invalid);
        Active = IntVec3.Invalid;
        ActiveAmmo = null;
        Fired = false;
    }
}

public sealed class BarrageOrder : IExposable
{
    public BarrageMode Mode;
    public List<GunQueue> Guns = new();
    private List<IntVec3> path = new();
    private HashSet<IntVec3> remaining;
    private Map map;
    private bool dirty = true;
    public int Unreachable { get; private set; }
    public int RemainingCells => remaining?.Count ?? path.Count;
    public int PlannedRounds => Guns.Sum(s => s.PendingRounds);

    public BarrageOrder() { }

    internal BarrageOrder(Map map, BarrageMode mode, List<IntVec3> cells, IEnumerable<Building_Turret> guns)
    {
        Mode = mode;
        path = cells;
        Guns = guns.Select(g => new GunQueue { Gun = g }).ToList();
        Attach(map);
        Replan();
    }

    internal void Attach(Map owner)
    {
        map = owner;
        remaining = new HashSet<IntVec3>(path);
        Guns.RemoveAll(s => s?.Gun == null);
        dirty = true;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref Mode, "mode");
        List<IntVec3> cells = Scribe.mode == LoadSaveMode.Saving && remaining != null
            ? path.Where(remaining.Contains).ToList() : path;
        Scribe_Collections.Look(ref cells, "remainingCells", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.LoadingVars) path = cells ?? new List<IntVec3>();
        Scribe_Collections.Look(ref Guns, "guns", LookMode.Deep);
        Guns ??= new List<GunQueue>();
    }

    internal void Remove(Building_Turret turret)
    {
        GunQueue slot = Guns.Find(s => s.Gun == turret);
        if (slot == null) return;
        slot.Release();
        Guns.Remove(slot);
        dirty = true;
    }

    internal string Inspect(Building_Turret turret)
    {
        GunQueue s = Guns.Find(q => q.Gun == turret);
        if (s == null) return null;
        string progress = "BO_Progress".Translate(s.Sent, s.PendingRounds);
        if (GunPort.Holding(turret)) return progress + "\n" + "BO_OnHold".Translate();
        if (GunPort.Actual(turret) == null) return progress + "\n" + "BO_NeedsAmmo".Translate();
        if (!GunPort.Staffed(turret)) return progress + "\n" + "BO_NeedsCrew".Translate();
        return progress;
    }

    private static int ReadPriority(GunQueue slot)
    {
        Building_Turret gun = slot.Gun;
        if (GunPort.Holding(gun) || gun.Position.Roofed(gun.Map)) return 3;
        if (!GunPort.Staffed(gun)) return 2;
        return GunPort.Actual(gun) == null ? 1 : 0;
    }

    private void Replan()
    {
        var blank = new HashSet<IntVec3>(remaining);
        int marks = 0;
        foreach (GunQueue s in Guns)
        {
            s.Next.Clear();
            s.Expected = GunPort.Forecast(s.Gun, s.Expected);
            s.Priority = ReadPriority(s);
            s.Burst = GunPort.BurstSize(s.Gun.AttackVerb);
            if (!s.Active.IsValid || s.Fired) continue;
            s.Wave = 0;
            GunPort.VisitFootprint(map, s.ActiveAmmo, s.Active, c => blank.Remove(c));
            marks++;
        }
        foreach (IntVec3 cell in path)
        {
            if (!blank.Contains(cell)) continue;
            GunQueue chosen = null;
            foreach (GunQueue s in Guns)
            {
                if (s.Expected == null || !GunPort.CanPaint(s.Gun, cell, s.Priority != 0)) continue;
                if (chosen == null || s.Priority < chosen.Priority ||
                    (s.Priority == chosen.Priority && s.Sent + s.PendingRounds < chosen.Sent + chosen.PendingRounds))
                    chosen = s;
            }
            if (chosen == null) continue;
            chosen.Next.Enqueue((cell, marks++ / Guns.Count));
            GunPort.VisitFootprint(map, chosen.Expected, cell, c => blank.Remove(c));
        }
        Unreachable = blank.Count;
        dirty = false;
    }

    private void Observe(GunQueue slot)
    {
        var gun = slot.Gun;
        var verb = gun.AttackVerb;
        if (!slot.Active.IsValid) return;
        if (verb.LastShotTick != slot.SeenShot && verb.CurrentTarget == new LocalTargetInfo(slot.Active))
        {
            slot.SeenShot = verb.LastShotTick;
            slot.Sent++;
            if (!slot.Fired)
            {
                GunPort.VisitFootprint(map, slot.ActiveAmmo, slot.Active, c => remaining.Remove(c));
                slot.Fired = true;
            }
        }
        if (slot.Fired && !verb.Bursting) slot.Release();
    }

    internal bool Tick()
    {
        int now = Find.TickManager.TicksGame;
        for (int i = Guns.Count - 1; i >= 0; i--)
        {
            GunQueue s = Guns[i];
            Building_Turret gun = s.Gun;
            if (gun?.Spawned != true || gun.Map != map || !GunPort.Supported(gun))
            {
                s.Release();
                Guns.RemoveAt(i);
                dirty = true;
                continue;
            }
            bool manual = gun.ForcedTarget.IsValid
                ? gun.ForcedTarget != new LocalTargetInfo(s.Active)
                : s.Active.IsValid && !GunPort.Holding(gun);
            Observe(s);
            if (manual)
            {
                // A player's replacement target (or the vanilla stop button) owns this turret now.
                Guns.RemoveAt(i);
                dirty = true;
                continue;
            }
            if (!dirty && !s.Active.IsValid && now % 15 != 0) continue;
            ThingDef actual = GunPort.Actual(gun);
            if (s.Active.IsValid && !s.Fired && actual != null && actual != s.ActiveAmmo)
            {
                s.ActiveAmmo = actual;
                dirty = true;
            }
            int priority = ReadPriority(s);
            ThingDef expected = actual ?? GunPort.Forecast(gun, s.Expected);
            if (priority != s.Priority || expected != s.Expected) dirty = true;
            if (now % 15 == 0 && GunPort.BurstSize(gun.AttackVerb) != s.Burst) dirty = true;
            if (s.Active.IsValid && !s.Fired && priority != 0)
            {
                s.Release();
                dirty = true;
            }
        }
        if (Guns.Count == 0) return true;
        if (dirty) Replan();
        if (Guns.All(s => !s.Active.IsValid && s.Next.Count == 0))
        {
            if (RemainingCells > 0 && Guns.Any(s => s.Expected == null)) return false;
            if (RemainingCells > 0)
                Messages.Message("BO_Skipped".Translate(RemainingCells), MessageTypeDefOf.NeutralEvent, false);
            return true;
        }
        if (now % 5 != 0) return false;
        int front = Mode == BarrageMode.Creeping
            ? Guns.Min(s => s.Active.IsValid ? s.Wave : s.Next.TryPeek(out var item) ? item.wave : int.MaxValue)
            : int.MaxValue;
        for (int i = 0; i < Guns.Count; i++)
        {
            GunQueue s = Guns[i];
            if (s.Active.IsValid || !s.Next.TryPeek(out var next) || next.wave > front || ReadPriority(s) != 0) continue;
            var gun = s.Gun;
            ThingDef actual = GunPort.Actual(gun);
            if (actual != s.Expected || !GunPort.CanPaint(gun, next.cell)) { dirty = true; continue; }
            if (gun.AttackVerb.Bursting) continue;
            s.Next.Dequeue();
            s.Active = next.cell;
            s.Wave = next.wave;
            s.ActiveAmmo = actual;
            s.SeenShot = gun.AttackVerb.LastShotTick;
            gun.OrderAttack(next.cell);
            if (gun.ForcedTarget != new LocalTargetInfo(next.cell))
            {
                s.Release();
                Guns.RemoveAt(i--);
                dirty = true;
            }
        }
        return false;
    }
}
