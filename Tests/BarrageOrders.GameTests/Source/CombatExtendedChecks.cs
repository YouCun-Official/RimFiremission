using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace BarrageOrders.GameTests;

public sealed class CombatExtendedChecks : MapComponent
{
    private sealed class Lane
    {
        internal Building_Turret Gun;
        internal Pawn Operator;
        internal object Ammo;
        internal ThingDef Shell;
        internal ThingDef Chambered;
        internal int Shots, Supplied, Seen, DrySince;
        internal int Count => (int)Prop(Ammo, "CurMagCount");
    }
    private readonly List<Lane> lanes = new();
    private static readonly HashSet<string> checks = new();
    private List<IntVec3> area;
    private readonly HashSet<IntVec3> actualCoverage = new();
    private IntVec3 center;
    private int phase, start;
    private static string Report => Path.Combine(GenFilePaths.SaveDataFolderPath, "BarrageOrders-ce-tests.txt");
    private BarrageMap Orders => map.GetComponent<BarrageMap>();
    private bool Allowed => ModsConfig.IsActive("ceteam.combatextended") && Find.CurrentMap == map &&
        Current.ProgramState == ProgramState.Playing && Directory.GetParent(GenFilePaths.SaveDataFolderPath)?.Name == "test-savedata";
    public CombatExtendedChecks(Map map) : base(map) { }

    private static object Prop(object obj, string name) => obj.GetType().GetProperty(name).GetValue(obj);
    private static List<object> AmmoLinks(object comp)
    {
        var set = Prop(comp, "CurAmmoSet");
        var entries = (IEnumerable)set.GetType().GetField("ammoTypes").GetValue(set);
        return entries.Cast<object>().ToList();
    }
    private static ThingDef LinkDef(object link, string name) => (ThingDef)link.GetType().GetField(name).GetValue(link);
    private static void SetSelected(Lane lane, ThingDef ammo) => lane.Ammo.GetType().GetProperty("SelectedAmmo").SetValue(lane.Ammo, ammo);
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception(label);
        if (checks.Add(label)) File.AppendAllText(Report, "PASS " + label + Environment.NewLine);
    }

    private static void Fill(Lane lane)
    {
        var item = ThingMaker.MakeThing(lane.Shell);
        item.stackCount = 1;
        SetSelected(lane, lane.Shell);
        int before = lane.Count;
        lane.Ammo.GetType().GetMethod("LoadAmmo", new[] { typeof(Thing), typeof(bool) })
            .Invoke(lane.Ammo, new object[] { item, false });
        Check(lane.Count == before + 1, "CE LoadAmmo inserted one actual inventory item");
        lane.Chambered = (ThingDef)Prop(lane.Ammo, "CurAmmoProjectile");
        lane.Supplied++;
    }

    private void RecordCoverage(ThingDef projectile, IntVec3 cell)
    {
        actualCoverage.Add(cell);
        var props = projectile.projectile;
        if (props.explosionRadius > 0 && props.damageDef != null)
            actualCoverage.UnionWith(props.damageDef.Worker.ExplosionCellsToHit(cell, map, props.explosionRadius));
        if (projectile.comps == null) return;
        foreach (var comp in projectile.comps)
        {
            var type = comp.GetType();
            if (type.GetField("explosiveRadius")?.GetValue(comp) is float size && size > 0 &&
                type.GetField("explosiveDamageType")?.GetValue(comp) is DamageDef damage)
                actualCoverage.UnionWith(damage.Worker.ExplosionCellsToHit(cell, map, size));
        }
    }

    private Lane Spawn(string defName, IntVec3 where)
    {
        var def = DefDatabase<ThingDef>.GetNamed(defName);
        var gun = (Building_Turret)ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
        gun.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(gun, where, map);
        var ammo = Prop(gun, "CompAmmo");
        Check(ammo?.GetType().FullName == "CombatExtended.CompAmmoUser" && (bool)Prop(ammo, "UseAmmo"), "real CE ammo system is enabled");
        var lane = new Lane { Gun = gun, Ammo = ammo, Operator = TestCrew.Spawn(gun), Seen = gun.AttackVerb.LastShotTick };
        var links = AmmoLinks(ammo);
        var link = links.FirstOrDefault(l => LinkDef(l, "ammo").defName.Contains("EMP")) ??
                   links.First(l => LinkDef(l, "projectile").projectile != null);
        lane.Shell = LinkDef(link, "ammo");
        if (lane.Count > 0)
            ammo.GetType().GetMethod("TryUnload", new[] { typeof(bool) }).Invoke(ammo, new object[] { true });
        SetSelected(lane, lane.Shell);
        lanes.Add(lane);
        return lane;
    }

    private void Crew()
    {
        foreach (var lane in lanes) lane.Gun.TryGetComp<CompMannable>()?.ManForATick(lane.Operator);
    }

    private void SetUp()
    {
        var pack = LoadedModManager.RunningModsListForReading.First(m => m.PackageId.Equals("ceteam.combatextended", StringComparison.OrdinalIgnoreCase) ||
            m.PackageIdPlayerFacing.Equals("ceteam.combatextended", StringComparison.OrdinalIgnoreCase));
        File.AppendAllText(Report, $"CE source {pack.RootDir}; RimWorld {VersionControl.CurrentVersionString}\n");
        var site = new IntVec3(16, 0, 16);
        center = new IntVec3(68, 0, 46);
        foreach (var region in new[] { CellRect.CenteredOn(site, 9), CellRect.CenteredOn(center, 18) })
            foreach (var c in region.ClipInsideMap(map))
            {
                map.roofGrid.SetRoof(c, null);
                foreach (var thing in c.GetThingList(map).ToArray())
                    if (thing is Building or Plant) thing.DeSpawn();
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete);
            }
        Spawn("Turret_Mortar", site);
        Spawn("Turret_Mortar", site + IntVec3.East * 5);
        Check(lanes.All(l => !GunPort.Staffed(l.Gun)), "unmanned CE turrets wait for a gunner");
        Check(lanes.All(l => l.Gun.GetGizmos().OfType<Command_Action>().Any(c => c.defaultLabel == "BO_Circle".Translate())),
            "CE turrets show area controls alongside native commands");
        Check(lanes.All(l => l.Count == 0 && GunPort.Actual(l.Gun) == null && GunPort.Forecast(l.Gun, null) == (ThingDef)Prop(l.Ammo, "SelectedAmmoProjectile")),
            "empty CE magazine reports selected shell as forecast only");
        area = CellRect.FromLimits(center, center + new IntVec3(12, 0, 9)).Cells.ToList();
        Check(Orders.Start(BarrageMode.Creeping, area, lanes.Select(l => l.Gun)), "two empty CE mortars can queue a rectangle");
        Crew();
        start = GenTicks.TicksGame;
        phase = 1;
    }

    public override void MapComponentUpdate()
    {
        if (!Allowed || phase >= 9 || LongEventHandler.AnyEventNowOrWaiting) return;
        try
        {
            if (phase == 0) SetUp();
            if (phase < 9) Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void Fail(Exception ex)
    {
        phase = 99;
        Find.TickManager.Pause();
        File.AppendAllText(Report, "FAIL " + ex + Environment.NewLine);
        Log.Error("[BarrageOrders.GameTests] " + ex);
    }

    public override void MapComponentTick()
    {
        if (!Allowed || phase == 0 || phase >= 9) return;
        try
        {
            int now = GenTicks.TicksGame;
            Crew();
            if (now - start > 45000) throw new Exception("CE integration timed out in phase " + phase);
            if (phase == 1)
            {
                if (now - start < 30) return;
                Check(lanes.All(l => l.Count == 0 && l.Gun.AttackVerb.LastShotTick == l.Seen && !l.Gun.ForcedTarget.IsValid),
                    "empty CE magazines do not issue or advance casts");
                var first = lanes[0];
                var other = AmmoLinks(first.Ammo).First(x => LinkDef(x, "ammo") != first.Shell);
                if (now - start == 30) SetSelected(first, LinkDef(other, "ammo"));
                if (now - start < 65) return;
                Check(Orders.For(first.Gun).Guns.Single(q => q.Gun == first.Gun).Expected == LinkDef(other, "projectile"),
                      "changing a dry gun's CE selection updates its queued forecast");
                foreach (var l in lanes) Fill(l);
                SetSelected(first, LinkDef(other, "ammo"));
                Check(GunPort.Actual(first.Gun) == (ThingDef)Prop(first.Ammo, "CurAmmoProjectile") &&
                      GunPort.Actual(first.Gun) != (ThingDef)Prop(first.Ammo, "SelectedAmmoProjectile"),
                      "selection for the next CE reload does not replace the chambered projectile");
                SetSelected(first, first.Shell);
                phase = 2;
            }
            foreach (var lane in lanes)
            {
                var verb = lane.Gun.AttackVerb;
                if (verb.LastShotTick != lane.Seen)
                {
                    lane.Seen = verb.LastShotTick;
                    lane.Shots++;
                    lane.DrySince = now;
                    var slot = Orders.For(lane.Gun)?.Guns.Find(q => q.Gun == lane.Gun);
                    File.AppendAllText(Report, $"tick {now} {lane.Gun.ThingID} cast {verb.CurrentTarget}; forced {lane.Gun.ForcedTarget}; queued {slot?.Active}; magazine {lane.Count}\n");
                    Check(area.Contains(verb.CurrentTarget.Cell), "CE cast target belongs to selected area");
                    if (phase == 2) RecordCoverage(lane.Chambered, verb.CurrentTarget.Cell);
                }
                if (phase >= 2 && Orders.For(lane.Gun) != null && lane.Count == 0 && now - lane.DrySince > 35)
                    Fill(lane);
            }
            if (phase == 2 && lanes.All(l => Orders.For(l.Gun) == null))
            {
                Check(lanes.All(l => l.Shots > 0 && l.Supplied - l.Count == l.Shots && !l.Gun.ForcedTarget.IsValid),
                    "both real CE guns consumed exactly one item per cast and stopped");
                Check(Math.Abs(lanes[0].Shots - lanes[1].Shots) <= 1, "real CE group work balanced to one round");
                Check(area.All(actualCoverage.Contains), "chambered CE projectiles cover every selected tile at their actual aim cells");
                File.AppendAllText(Report, $"CE completed rectangle {lanes[0].Shots}+{lanes[1].Shots}\n");
                var third = DefDatabase<ThingDef>.GetNamedSilentFail("PLA_Turret_PP93Mortar");
                var gun = third == null ? lanes[0] : Spawn(third.defName, new IntVec3(16, 0, 22));
                Crew();
                if (gun.Count == 0) Fill(gun);
                var ammoOptions = AmmoLinks(gun.Ammo);
                var alternate = ammoOptions.FirstOrDefault(l => LinkDef(l, "ammo") != gun.Shell);
                if (alternate != null)
                {
                    gun.Ammo.GetType().GetMethod("TryUnload", new[] { typeof(bool) }).Invoke(gun.Ammo, new object[] { true });
                    gun.Shell = LinkDef(alternate, "ammo");
                    Fill(gun);
                    Check(GunPort.Actual(gun.Gun) == LinkDef(alternate, "projectile"), "changing CE shell changes projectile used by the order");
                }
                area = new List<IntVec3> { center };
                Check(Orders.Start(BarrageMode.Suppression, area, new[] { gun.Gun }), "CE single-cell circle order accepted");
                if (third != null) Check(CombatExtendedPort.Handles(gun.Gun), "installed PLA-Armory turret actually uses CE turret code");
                phase = 3;
            }
            else if (phase == 3 && lanes.All(l => Orders.For(l.Gun) == null))
            {
                var gun = lanes.Count > 2 ? lanes[2] : lanes[0];
                Check(gun.Shots > 0 && gun.Gun.AttackVerb.CurrentTarget.Cell == center && !gun.Gun.ForcedTarget.IsValid,
                    "CE suppression order fired and released the target");
                phase = 9;
                File.AppendAllText(Report, "COMPLETE " + string.Join(", ", lanes.Select(l => l.Gun.def.defName + "=" + l.Shots)) + Environment.NewLine);
                Find.TickManager.Pause();
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
}
