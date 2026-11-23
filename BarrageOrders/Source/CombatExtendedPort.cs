using System;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace BarrageOrders;

// Keep vanilla loadable without an assembly reference to optional Combat Extended.
internal static class CombatExtendedPort
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Assembly Assembly = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == "CombatExtended");
    private static readonly Type Turret = Assembly?.GetType("CombatExtended.Building_TurretGunCE");
    private static readonly Type Ammo = Assembly?.GetType("CombatExtended.CompAmmoUser");
    private static readonly Type Explosive = Assembly?.GetType("CombatExtended.CompProperties_ExplosiveCE");
    private static readonly PropertyInfo CanForce = Turret?.GetProperty("CanSetForcedTarget", Instance);
    private static readonly PropertyInfo IsActive = Turret?.GetProperty("Active", Instance);
    private static readonly FieldInfo Hold = Turret?.GetField("holdFire", Instance);
    private static readonly FieldInfo Reload = Turret?.GetField("isReloading", Instance);
    private static readonly PropertyInfo AmmoComp = Turret?.GetProperty("CompAmmo", Instance);
    private static readonly PropertyInfo Projectile = Turret?.GetProperty("Projectile", Instance);
    private static readonly PropertyInfo Ready = Ammo?.GetProperty("CanBeFiredNow", Instance);
    private static readonly PropertyInfo Selected = Ammo?.GetProperty("SelectedAmmoProjectile", Instance);
    private static readonly FieldInfo Radius = Explosive?.GetField("explosiveRadius", Instance);
    private static readonly FieldInfo Damage = Explosive?.GetField("explosiveDamageType", Instance);

    internal static bool Handles(Building_Turret gun) => Turret?.IsInstanceOfType(gun) == true;
    internal static bool AllowsOrders(Building_Turret gun) => CanForce?.GetValue(gun) is true;
    internal static bool Active(Building_Turret gun) => IsActive?.GetValue(gun) is not false;
    internal static bool Holding(Building_Turret gun) => Hold?.GetValue(gun) is true;

    internal static ThingDef LoadedProjectile(Building_Turret gun)
    {
        var ammo = AmmoComp?.GetValue(gun);
        if (Reload?.GetValue(gun) is true || (ammo != null && Ready?.GetValue(ammo) is not true)) return null;
        return Projectile?.GetValue(gun) as ThingDef;
    }

    internal static ThingDef NextProjectile(Building_Turret gun)
    {
        var ammo = AmmoComp?.GetValue(gun);
        return (ammo == null ? null : Selected?.GetValue(ammo) as ThingDef) ?? Projectile?.GetValue(gun) as ThingDef;
    }

    internal static void VisitSecondaryEffect(Map map, ThingDef projectile, IntVec3 cell, Action<IntVec3> visit)
    {
        if (Explosive == null || projectile.comps == null) return;
        foreach (var comp in projectile.comps)
            if (Explosive.IsInstanceOfType(comp) && Radius?.GetValue(comp) is float radius && radius > 0f &&
                Damage?.GetValue(comp) is DamageDef damage)
                foreach (var tile in damage.Worker.ExplosionCellsToHit(cell, map, radius)) visit(tile);
    }
}
