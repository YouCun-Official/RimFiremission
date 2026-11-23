using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BarrageOrders;

[StaticConstructorOnStartup]
internal static class BarrageRegistration
{
    static BarrageRegistration()
    {
        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            if (!typeof(Building_Turret).IsAssignableFrom(def.thingClass)) continue;
            if (def.comps?.Any(c => c.compClass == typeof(BarrageControls)) == true) continue;
            def.comps = new List<CompProperties>(def.comps ?? new List<CompProperties>())
                { new CompProperties(typeof(BarrageControls)) };
        }
    }
}

public sealed class BarrageControls : ThingComp
{
    private Building_Turret Turret => (Building_Turret)parent;

    internal static List<Building_Turret> Selection(Building_Turret origin) =>
        Find.Selector.SelectedObjects.OfType<Building_Turret>().Append(origin)
            .Where(t => t.Map == origin.Map && GunPort.Supported(t)).Distinct()
            .OrderBy(t => t.thingIDNumber).ToList();

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        if (!GunPort.Supported(Turret)) yield break;
        yield return AreaCommand(BarrageMode.Creeping);
        yield return AreaCommand(BarrageMode.Suppression);
        BarrageMap orders = parent.Map.GetComponent<BarrageMap>();
        if (orders?.For(Turret) != null)
            yield return new Command_Action
            {
                defaultLabel = "BO_Stop".Translate(), defaultDesc = "BO_StopDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt"), alsoClickIfOtherInGroupClicked = false,
                action = () => orders.Cancel(Selection(Turret))
            };
    }

    private Command_Action AreaCommand(BarrageMode mode)
    {
        string key = mode == BarrageMode.Creeping ? "BO_Creep" : "BO_Circle";
        string icon = mode == BarrageMode.Creeping ? "RectangleFilled" : "OvalFilled";
        return new Command_Action
        {
            defaultLabel = key.Translate(), defaultDesc = (key + "Desc").Translate(),
            icon = ContentFinder<Texture2D>.Get("UI/Widgets/DrawStyles/" + icon),
            alsoClickIfOtherInGroupClicked = false,
            action = () => Find.DesignatorManager.Select(new BarrageDesignator(mode, parent.Map, Selection(Turret)))
        };
    }

    public override string CompInspectStringExtra() =>
        parent.Map?.GetComponent<BarrageMap>()?.For(Turret)?.Inspect(Turret);

    public override void PostDrawExtraSelectionOverlays()
    {
        var order = parent.Map?.GetComponent<BarrageMap>()?.For(Turret);
        var slot = order?.Guns.Find(s => s.Gun == Turret);
        if (slot == null) return;
        foreach (var next in slot.Next) CellRenderer.RenderSpot(next.cell, 0.53f, 0.31f);
        if (slot.Active.IsValid) CellRenderer.RenderSpot(slot.Active, 0.12f, 0.55f);
    }
}

public sealed class BarrageDesignator : Designator
{
    private readonly BarrageMode mode;
    private readonly Map owner;
    private readonly List<Building_Turret> guns;
    public IntVec3 Origin = IntVec3.Invalid;

    public BarrageDesignator(BarrageMode mode, Map owner, List<Building_Turret> guns)
    {
        this.mode = mode;
        this.owner = owner;
        this.guns = guns;
        defaultLabel = (mode == BarrageMode.Creeping ? "BO_Creep" : "BO_Circle").Translate();
        soundDragChanged = SoundDefOf.Designate_DragZone_Changed;
        soundDragSustain = SoundDefOf.Designate_DragAreaAdd;
    }

    public override DrawStyleCategoryDef DrawStyleCategory =>
        DefDatabase<DrawStyleCategoryDef>.GetNamed(mode == BarrageMode.Creeping ? "BO_Rectangle" : "BO_Circle");
    public override bool DragDrawMeasurements => mode == BarrageMode.Creeping;
    public override bool CanRemainSelected() =>
        Find.CurrentMap == owner && guns.Any(t => t.Spawned && t.Map == owner);
    public override AcceptanceReport CanDesignateCell(IntVec3 cell) => cell.InBounds(owner);
    public override void RenderHighlight(List<IntVec3> cells)
    {
        foreach (IntVec3 c in cells) CellRenderer.RenderCell(c, mode == BarrageMode.Creeping ? 0.33f : 0.58f);
        GenDraw.DrawFieldEdges(cells, Color.white);
    }

    public override void DrawMouseAttachments()
    {
        GenUI.DrawMouseAttachment(null, (mode == BarrageMode.Creeping ? "BO_RectHint" : "BO_CircleHint").Translate());
    }

    public override void DesignateSingleCell(IntVec3 c) => DesignateMultiCell(new[] { c });

    public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
    {
        var selected = cells.Where(c => c.InBounds(owner)).Distinct().ToList();
        if (mode == BarrageMode.Suppression && Origin.IsValid)
            selected = selected.OrderBy(c => c.DistanceToSquared(Origin)).ToList();
        owner.GetComponent<BarrageMap>().Start(mode, selected, guns);
        Find.DesignatorManager.Deselect();
    }
}
