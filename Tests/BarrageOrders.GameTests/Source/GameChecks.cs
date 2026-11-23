using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace BarrageOrders.GameTests;

public sealed class FixtureTurret : Building_TurretGun { }

public sealed class FixtureBurst : Verb_Shoot
{
    protected override int ShotsPerBurst => 2;
}

// This assembly is a separate test mod. The save-directory guard confines its fixtures to MCP sessions.
public sealed class GameChecks : MapComponent
{
    private const string SaveName = "BarrageOrders-acceptance";
    private int phase, startTick, rememberedShots, observedA, observedB, lastA, lastB;
    private Building_TurretGun a, b, fixedGun;
    private List<Pawn> operators = new();
    private IntVec3 area;
    private List<IntVec3> orderedCells;
    private Building_TurretGun workshopGun;
    private int workshopShot;
    private int burstStamp, burstShots;
    private int inputStep;
    private bool checkedHold;
    private static bool loadedFromTestSave;
    private static readonly HashSet<string> logged = new();
    private static string Report => Path.Combine(GenFilePaths.SaveDataFolderPath, "BarrageOrders-tests.txt");
    private BarrageMap Orders => map.GetComponent<BarrageMap>();
    private bool Allowed => Directory.GetParent(GenFilePaths.SaveDataFolderPath)?.Name == "test-savedata" &&
                            Find.CurrentMap == map && Current.ProgramState == ProgramState.Playing &&
                            !ModsConfig.IsActive("ceteam.combatextended");
    public GameChecks(Map map) : base(map) { }

    public override void ExposeData()
    {
        Scribe_Values.Look(ref phase, "phase");
        Scribe_Values.Look(ref startTick, "startTick");
        Scribe_Values.Look(ref rememberedShots, "rememberedShots");
        Scribe_Values.Look(ref observedA, "observedA");
        Scribe_Values.Look(ref observedB, "observedB");
        Scribe_Values.Look(ref lastA, "lastA");
        Scribe_Values.Look(ref lastB, "lastB");
        Scribe_Values.Look(ref area, "area");
        Scribe_Collections.Look(ref orderedCells, "orderedCells", LookMode.Value);
        Scribe_References.Look(ref a, "a");
        Scribe_References.Look(ref b, "b");
        Scribe_References.Look(ref fixedGun, "fixedGun");
        Scribe_Collections.Look(ref operators, "testGunners", LookMode.Reference);
        if (Scribe.mode == LoadSaveMode.PostLoadInit && phase == 2) loadedFromTestSave = true;
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        if (logged.Add(label)) File.AppendAllText(Report, "PASS " + label + Environment.NewLine);
    }

    private void Fail(Exception ex)
    {
        phase = 99;
        File.AppendAllText(Report, "FAIL " + ex + Environment.NewLine);
        Log.Error("[BarrageOrders.GameTests] " + ex);
    }

    public override void MapComponentUpdate()
    {
        if (!Allowed || phase >= 9 || LongEventHandler.AnyEventNowOrWaiting) return;
        try
        {
            if (phase == 0)
            {
                phase = 1;
                Setup();
            }
            else if (phase == 2 && loadedFromTestSave)
            {
                Check(Orders.For(a) != null && Orders.For(a) == Orders.For(b), "same shared order restored from a real save");
                Check(Orders.For(a).Guns.Sum(s => s.Sent) >= rememberedShots, "fired count persisted across reload");
                phase = 3;
            }
            if (phase == 6) CheckInput();
            else if (phase != 2) Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
        }
        catch (Exception ex) { Fail(ex); }
    }

    private Building_TurretGun Spawn(string name, IntVec3 c)
    {
        var def = DefDatabase<ThingDef>.GetNamed(name);
        var t = (Building_TurretGun)ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
        t.SetFaction(Faction.OfPlayer);
        return (Building_TurretGun)GenSpawn.Spawn(t, c, map);
    }

    private void Crew()
    {
        a.GetComp<CompMannable>().ManForATick(operators[0]);
        b.GetComp<CompMannable>().ManForATick(operators[1]);
        fixedGun.GetComp<CompMannable>().ManForATick(operators[2]);
    }

    private static void Load(Building_TurretGun t, string def, int count)
    {
        var shell = DefDatabase<ThingDef>.GetNamed(def);
        var loader = t.gun.TryGetComp<CompChangeableProjectile>();
        loader.allowedShellsSettings.filter.SetAllow(shell, true);
        loader.LoadShell(shell, count);
    }

    private void Setup()
    {
        File.AppendAllText(Report, "RimWorld " + VersionControl.CurrentVersionString + Environment.NewLine);
        var gunSite = new IntVec3(16, 0, 16);
        area = new IntVec3(64, 0, 47);
        foreach (var region in new[] { CellRect.CenteredOn(gunSite, 10), CellRect.CenteredOn(area, 23) })
        {
            foreach (var cell in region.ClipInsideMap(map))
            {
                map.roofGrid.SetRoof(cell, null);
                foreach (var thing in cell.GetThingList(map).ToArray())
                    if (thing is Building or Plant) thing.DeSpawn();
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
            }
        }
        a = Spawn("Turret_Mortar", gunSite);
        b = Spawn("BOQA_OtherMortar", gunSite + IntVec3.East * 4);
        fixedGun = Spawn("BOQA_FixedTurret", gunSite + IntVec3.North * 4);
        foreach (var gun in new[] { a, b, fixedGun }) operators.Add(TestCrew.Spawn(gun));
        Crew();
        Load(a, "Shell_Firefoam", 30);
        Load(b, "Shell_Firefoam", 30);
        Check(a.GetGizmos().OfType<Command_VerbTarget>().Any(), "original single-target command remains");
        Check(GunPort.Supported(a) && GunPort.Supported(b) && GunPort.Supported(fixedGun), "native, other-def subclass and fixed-projectile turret accepted");
        var ordinary = Spawn("Turret_MiniTurret", gunSite + IntVec3.West * 5);
        Check(!GunPort.Supported(ordinary), "direct-fire turret excluded");
        ordinary.Destroy();
        var corners = new List<IntVec3>();
        new RectangleBrush().Update(area + new IntVec3(7, 0, 3), area, corners);
        Check(corners.Count == 32 && corners.First().x == area.x + 7 && corners.Last().x == area.x,
              "reverse rectangle starts at pressed edge");
        var circle = new List<IntVec3>();
        new CircleBrush().Update(area, area + IntVec3.East * 3, circle);
        Check(circle.Count == 29 && circle.Contains(area) && !circle.Contains(area + new IntVec3(3, 0, 3)), "filled centre/radius brush");
        circle.Clear();
        new CircleBrush().Update(IntVec3.Zero, IntVec3.East * 4, circle);
        Check(circle.All(c => c.InBounds(map)) && circle.Count == 17, "circle clipped at map corner");
        circle.Clear();
        new CircleBrush().Update(area, area, circle);
        Check(circle.SequenceEqual(new[] { area }), "zero-length circle selects one cell");
        Check(!Orders.Start(BarrageMode.Creeping, new List<IntVec3> { a.Position + IntVec3.East }, new[] { a }), "native minimum range respected");
        var bad = area + IntVec3.North;
        map.roofGrid.SetRoof(bad, RoofDefOf.RoofRockThick);
        Check(!Orders.Start(BarrageMode.Suppression, new List<IntVec3> { bad }, new[] { a }), "overhead obstruction rejected");
        map.roofGrid.SetRoof(bad, null);

        DrawFromCommand(BarrageMode.Suppression, area, area + IntVec3.East * 10, a, fixedGun);
        Check(Orders.For(a).Guns.Count == 2 && Orders.For(a).Guns.All(s => s.Next.Count > 0), "selected group shares one circle order");
        Cover(Orders.For(a), orderedCells);
        Orders.Cancel(new[] { fixedGun });
        Orders.For(a).Tick();
        Check(Orders.For(fixedGun) == null && Orders.For(a).Guns.Count == 1,
              "removing one selected gun leaves the other on the shared order");
        Cover(Orders.For(a), orderedCells);
        Orders.Cancel(new[] { a, fixedGun });
        Check(Orders.For(a) == null && Orders.For(fixedGun) == null, "cancel removes selected guns");

        var edge = area + new IntVec3(22, 0, 10);
        DrawFromCommand(BarrageMode.Creeping, area, edge, a, b);
        int foam = Orders.For(a).PlannedRounds;
        Cover(Orders.For(a), orderedCells);
        Check(Math.Abs(Orders.For(a).Guns[0].PendingRounds - Orders.For(a).Guns[1].PendingRounds) <= 1,
              "initial equal-profile queues differ by at most one round");
        Load(b, "Shell_HighExplosive", 30);
        Load(a, "Shell_HighExplosive", 30);
        DrawFromCommand(BarrageMode.Creeping, area, edge, a, b);
        Check(Orders.For(a).PlannedRounds > foam, "different native shell footprint changes the estimated count");
        Load(a, "Shell_Firefoam", 30);
        DrawFromCommand(BarrageMode.Creeping, area, edge, a, b);
        Cover(Orders.For(a), orderedCells);
        Check(Orders.For(a).Guns.Select(s => s.Expected).Distinct().Count() == 2, "each gun uses its own loaded shell");
        Load(b, "Shell_Firefoam", 30);
        DrawFromCommand(BarrageMode.Creeping, area, edge, a, b);
        lastA = a.AttackVerb.LastShotTick;
        lastB = b.AttackVerb.LastShotTick;
        startTick = GenTicks.TicksGame;
    }

    private void DrawFromCommand(BarrageMode mode, IntVec3 from, IntVec3 to, params Building_TurretGun[] selection)
    {
        var brush = SelectCommand(mode, selection);
        orderedCells = new List<IntVec3>();
        Find.DesignatorManager.SelectedStyle.DrawStyleWorker.Update(from, to, orderedCells);
        brush.DesignateMultiCell(orderedCells);
        Check(Find.DesignatorManager.SelectedDesignator == null, "release ends designation");
    }

    private static BarrageDesignator SelectCommand(BarrageMode mode, params Building_TurretGun[] selection)
    {
        Find.Selector.ClearSelection();
        foreach (var turret in selection) Find.Selector.Select(turret, false);
        string label = (mode == BarrageMode.Creeping ? "BO_Creep" : "BO_Circle").Translate();
        var command = selection[0].GetGizmos().OfType<Command_Action>().Single(c => c.defaultLabel == label);
        command.ProcessInput(new UnityEngine.Event());
        return (BarrageDesignator)Find.DesignatorManager.SelectedDesignator;
    }

    private void CheckInput()
    {
        Find.TickManager.Pause();
        var mode = inputStep < 4 ? BarrageMode.Creeping : BarrageMode.Suppression;
        var to = area + (mode == BarrageMode.Creeping ? new IntVec3(8, 0, -3) : new IntVec3(4, 0, 3));
        var manager = Find.DesignatorManager;
        switch (inputStep % 4)
        {
            case 0:
                SelectCommand(mode, a, b);
                break;
            case 1:
                GameInput.Deliver(area, UnityEngine.EventType.MouseDown);
                Check(manager.Dragger.Dragging, "input manager starts captured drag");
                break;
            case 2:
                GameInput.Deliver(to, UnityEngine.EventType.MouseDrag, inspect: () =>
                {
                    var actual = manager.Dragger.DragCells.ToHashSet();
                    var expected = (mode == BarrageMode.Creeping ? CellRect.FromLimits(area, to) : CellRect.CenteredOn(area, 5))
                        .Where(c => mode == BarrageMode.Creeping || c.DistanceToSquared(area) <= 25).ToHashSet();
                    Check(actual.SetEquals(expected), "native drag preview contains exact cells for " + mode);
                    manager.DesignatorManagerUpdate();
                });
                break;
            case 3:
                GameInput.Deliver(to, UnityEngine.EventType.MouseUp);
                Check(!manager.Dragger.Dragging && manager.SelectedDesignator == null &&
                      Orders.For(a)?.Mode == mode && Orders.For(a) == Orders.For(b), "native pointer release commits " + mode);
                Orders.Cancel(new[] { a, b });
                break;
        }
        if (++inputStep < 8) return;
        SelectCommand(BarrageMode.Creeping, a);
        GameInput.Deliver(area, UnityEngine.EventType.MouseDown, button: 1);
        Check(manager.SelectedDesignator == null && Orders.For(a) == null, "right-click cancels without issuing a task");
        burstStamp = fixedGun.AttackVerb.LastShotTick;
        DrawFromCommand(BarrageMode.Suppression, area, area, fixedGun);
        Check(Orders.For(fixedGun)?.PlannedRounds == 2, "custom verb burst size is used for the shot estimate");
        phase = 8;
        Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
    }

    private void StartWorkshopCheck()
    {
        var def = DefDatabase<ThingDef>.GetNamedSilentFail("PLA_Turret_PP93Mortar");
        if (def == null) { Finish(); return; }
        workshopGun = Spawn(def.defName, a.Position + IntVec3.West * 6);
        operators.Add(TestCrew.Spawn(workshopGun));
        var loader = workshopGun.gun.TryGetComp<CompChangeableProjectile>();
        var shell = loader.GetParentStoreSettings().filter.AllowedThingDefs.First(s => s.projectileWhenLoaded != null);
        Load(workshopGun, shell.defName, 1);
        workshopShot = workshopGun.AttackVerb.LastShotTick;
        Check(!Orders.Start(BarrageMode.Suppression, new List<IntVec3> { area }, new[] { workshopGun }),
              "shorter-range workshop turret rejects a cell the native mortar could reach");
        var reachable = area + IntVec3.West * 18 + IntVec3.South * 4;
        DrawFromCommand(BarrageMode.Suppression, reachable, reachable, workshopGun);
        Check(Orders.For(workshopGun)?.PlannedRounds == 1, "unmodified installed PLA-Armory mortar accepts an order");
        phase = 7;
        Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
    }

    private void Finish()
    {
        phase = 9;
        File.AppendAllText(Report, $"COMPLETE native={observedA} fixture={observedB}\n");
        Find.TickManager.Pause();
    }

    private void Cover(BarrageOrder order, List<IntVec3> selection)
    {
        var uncovered = selection.ToHashSet();
        int count = 0;
        foreach (var slot in order.Guns)
            foreach (var cell in slot.Next.Select(p => p.cell).Concat(slot.Active.IsValid ? new[] { slot.Active } : Array.Empty<IntVec3>()))
            {
                Check(selection.Contains(cell), "planned aim cells stay inside selected region");
                var props = slot.Expected.projectile;
                foreach (var hit in props.damageDef.Worker.ExplosionCellsToHit(cell, map, props.explosionRadius))
                    uncovered.Remove(hit);
                count++;
            }
        Check(uncovered.Count == 0 && order.Unreachable == 0, "independent game effect mask covers every selected cell");
        Check(count < selection.Count, "no separate round per tile");
    }

    private void Observe(Building_TurretGun gun, ref int stamp, ref int total)
    {
        if (gun.AttackVerb.LastShotTick == stamp) return;
        stamp = gun.AttackVerb.LastShotTick;
        total++;
        Check(orderedCells.Contains(gun.AttackVerb.CurrentTarget.Cell), "real native cast aimed inside the designated area");
    }

    public override void MapComponentTick()
    {
        if (!Allowed || phase is not (1 or 3 or 4 or 5 or 7 or 8)) return;
        try
        {
            Crew();
            if (phase == 8)
            {
                Observe(fixedGun, ref burstStamp, ref burstShots);
                if (Orders.For(fixedGun) == null)
                {
                    Check(burstShots == 2 && !fixedGun.ForcedTarget.IsValid, "both shots in a native custom burst finish exactly once");
                    StartWorkshopCheck();
                }
                else if (GenTicks.TicksGame - startTick > 55000) throw new Exception("Custom burst timed out.");
                return;
            }
            if (phase == 7)
            {
                workshopGun.GetComp<CompMannable>()?.ManForATick(operators[3]);
                if (workshopGun.AttackVerb.LastShotTick != workshopShot && Orders.For(workshopGun) == null)
                {
                    Check(workshopGun.gun.TryGetComp<CompChangeableProjectile>().loadedCount == 0 &&
                          !workshopGun.ForcedTarget.IsValid, "unmodified PLA-Armory mortar consumed the round and finished");
                    Finish();
                }
                else if (GenTicks.TicksGame - startTick > 55000) throw new Exception("Workshop cast timed out.");
                return;
            }
            if (GenTicks.TicksGame - startTick > 55000) throw new Exception("Timed out at phase " + phase);
            Observe(a, ref lastA, ref observedA);
            Observe(b, ref lastB, ref observedB);
            var order = Orders.For(a);
            if (phase == 1 && observedA + observedB >= 1 && order?.Guns.Any(s => s.Sent > 0) == true)
            {
                rememberedShots = order.Guns.Sum(s => s.Sent);
                phase = 2;
                GameDataSaveLoader.SaveGame(SaveName);
                Check(File.Exists(GenFilePaths.FilePathForSavedGame(SaveName)), "mid-order save written");
                GameDataSaveLoader.LoadGame(SaveName);
            }
            else if (phase == 3 && Orders.For(a) == null && Orders.For(b) == null)
            {
                Check(observedA > 0 && observedB > 0 && !a.ForcedTarget.IsValid && !b.ForcedTarget.IsValid,
                      "live group order fires from both guns and clears both native targets");
                Check(Math.Abs(observedA - observedB) <= 1, "completed staffed group balanced within one round");
                Check(60 - a.gun.TryGetComp<CompChangeableProjectile>().loadedCount -
                      b.gun.TryGetComp<CompChangeableProjectile>().loadedCount == observedA + observedB,
                      "real ammunition consumed exactly once per observed projectile");
                DrawFromCommand(BarrageMode.Suppression, area, area + IntVec3.North * 9, a);
                phase = 4;
            }
            else if (phase == 4 && a.ForcedTarget.IsValid)
            {
                if (!checkedHold)
                {
                    var hold = a.GetGizmos().OfType<Command_Toggle>().Single(c => c.defaultLabel == "CommandHoldFire".Translate());
                    hold.toggleAction();
                    order.Tick();
                    Check(GunPort.Holding(a) && Orders.For(a) == order && !a.ForcedTarget.IsValid,
                          "native hold-fire suspends an already issued aim without cancelling the order");
                    hold.toggleAction();
                    checkedHold = true;
                    return;
                }
                var manual = area + IntVec3.West * 13;
                a.OrderAttack(manual);
                phase = 5;
            }
            else if (phase == 5 && Orders.For(a) == null)
            {
                Check(a.ForcedTarget.Cell == area + IntVec3.West * 13, "manual single-target order takes precedence");
                a.OrderAttack(LocalTargetInfo.Invalid);
                phase = 6;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
}
