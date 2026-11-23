using RimWorld;
using Verse;

namespace BarrageOrders.GameTests;

internal static class TestCrew
{
    internal static Pawn Spawn(Building_Turret gun)
    {
        var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            forceGenerateNewPawn: true, mustBeCapableOfViolence: true, dontGiveWeapon: true);
        var pawn = PawnGenerator.GeneratePawn(request);
        GenSpawn.Spawn(pawn, gun.InteractionCell, gun.Map);
        pawn.drafter.Drafted = true;
        return pawn;
    }
}
