using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace BarrageOrders;

internal static class GunPort
{
    // RimWorld exposes the order publicly, but keeps its player permission virtual and protected.
    private static readonly Func<Building_TurretGun, bool> CanForce =
        typeof(Building_TurretGun).GetProperty("CanSetForcedTarget", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetGetMethod(true)?.CreateDelegate<Func<Building_TurretGun, bool>>();
    private static readonly FieldInfo HoldFire = typeof(Building_TurretGun)
        .GetField("holdFire", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo MortarVerb = typeof(VerbProperties)
        .GetField("isMortar", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Func<Verb, int> ShotsPerBurst = typeof(Verb)
        .GetProperty("ShotsPerBurst", BindingFlags.Instance | BindingFlags.NonPublic)
        ?.GetGetMethod(true)?.CreateDelegate<Func<Verb, int>>();
    private static readonly FieldInfo BurstLeft = typeof(Verb)
        .GetField("burstShotsLeft", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static int BurstSize(Verb verb) => Math.Max(1, ShotsPerBurst?.Invoke(verb) ?? verb.BurstShotCount);
    internal static int ShotsLeft(Verb verb) => verb.Bursting && BurstLeft?.GetValue(verb) is int count ? count : 0;

    internal static bool Supported(Building_Turret turret)
    {
        if (turret?.Spawned != true) return false;
        if (turret is Building_TurretGun native && (native.gun == null || CanForce?.Invoke(native) == false))
            return false;
        if (CombatExtendedPort.Handles(turret) && !CombatExtendedPort.AllowsOrders(turret)) return false;
        if (turret.Faction != Faction.OfPlayer &&
            turret.TryGetComp<CompMannable>()?.ManningPawn?.Faction != Faction.OfPlayer) return false;
        VerbProperties props = turret.AttackVerb?.verbProps;
        if (props?.targetParams?.canTargetLocations != true) return false;
        if (turret.def.building?.IsMortar == true || MortarVerb?.GetValue(props) is true) return true;
        ProjectileProperties projectile = Actual(turret)?.projectile ?? Forecast(turret, null)?.projectile;
        return projectile != null && (projectile.flyOverhead || (!props.requireLineOfSight && projectile.explosionRadius > 0));
    }

    internal static CompChangeableProjectile Loader(Building_Turret turret) =>
        (turret as Building_TurretGun)?.gun?.TryGetComp<CompChangeableProjectile>()
        ?? turret.AttackVerb?.EquipmentSource?.TryGetComp<CompChangeableProjectile>();

    internal static ThingDef Actual(Building_Turret turret)
    {
        if (CombatExtendedPort.Handles(turret)) return CombatExtendedPort.LoadedProjectile(turret);
        if (turret.AttackVerb is Verb_LaunchProjectile launch) return launch.Projectile;
        return Loader(turret)?.Projectile ?? turret.AttackVerb?.verbProps?.defaultProjectile;
    }

    internal static ThingDef Forecast(Building_Turret turret, ThingDef previous)
    {
        ThingDef loaded = Actual(turret);
        if (loaded != null) return loaded;
        if (CombatExtendedPort.Handles(turret)) return CombatExtendedPort.NextProjectile(turret) ?? previous;
        var loader = Loader(turret);
        if (loader?.allowedShellsSettings?.filter == null) return null;
        ThingDef candidate = null;
        foreach (ThingDef shell in loader.allowedShellsSettings.filter.AllowedThingDefs)
        {
            ThingDef next = shell.projectileWhenLoaded;
            if (next?.projectile == null || loader.GetParentStoreSettings()?.AllowedToAccept(shell) == false) continue;
            if (next == previous) return next;
            if (candidate == null || next.projectile.explosionRadius < candidate.projectile.explosionRadius ||
                (next.projectile.explosionRadius == candidate.projectile.explosionRadius && next.index < candidate.index))
                candidate = next;
        }
        return candidate;
    }

    internal static bool Holding(Building_Turret turret) =>
        CombatExtendedPort.Handles(turret) ? CombatExtendedPort.Holding(turret)
        : turret is Building_TurretGun && HoldFire?.GetValue(turret) is true;

    internal static bool Staffed(Building_Turret turret)
    {
        if (turret is Building_TurretGun native && !native.Active) return false;
        if (CombatExtendedPort.Handles(turret) && !CombatExtendedPort.Active(turret)) return false;
        var crew = turret.TryGetComp<CompMannable>();
        if (crew == null) return true;
        Pawn pawn = crew.ManningPawn;
        return crew.MannedNow && pawn is { Dead: false, Downed: false } && !pawn.WorkTagIsDisabled(WorkTags.Violent);
    }

    internal static bool CanPaint(Building_Turret turret, IntVec3 cell, bool waiting = false)
    {
        if (!cell.InBounds(turret.Map) || turret.Map.roofGrid.RoofAt(cell)?.isThickRoof == true) return false;
        Verb verb = turret.AttackVerb;
        // A waiting crew can reserve reachable map cells. Dispatch always checks the fully staffed verb.
        return waiting ? !verb.OutOfRange(turret.Position, cell, CellRect.SingleCell(cell)) : verb.CanHitTarget(cell);
    }

    internal static void VisitFootprint(Map map, ThingDef projectile, IntVec3 cell, Action<IntVec3> visit)
    {
        visit(cell);
        ProjectileProperties props = projectile?.projectile;
        if (props == null) return;
        CombatExtendedPort.VisitSecondaryEffect(map, projectile, cell, visit);
        if (!(props.explosionRadius > 0f)) return;
        // This is the game's own effect mask, including its map-specific obstructions.
        IEnumerable<IntVec3> cells = props.damageDef?.Worker is DamageWorker worker
            ? worker.ExplosionCellsToHit(cell, map, props.explosionRadius)
            : GenRadial.RadialCellsAround(cell, props.explosionRadius, true);
        foreach (IntVec3 affected in cells) visit(affected);
    }
}
