using System;
using System.Collections.Generic;
using Verse;

namespace BarrageOrders;

public enum BarrageMode { Creeping, Suppression }

public sealed class RectangleBrush : DrawStyle
{
    public override bool CanHaveDuplicates => false;

    public override void Update(IntVec3 origin, IntVec3 target, List<IntVec3> buffer)
    {
        Map map = Find.CurrentMap;
        if (map == null || !origin.InBounds(map)) return;
        CellRect bounds = CellRect.FromLimits(origin, target).ClipInsideMap(map);
        bool alongX = Math.Abs(target.x - origin.x) >= Math.Abs(target.z - origin.z);
        int dx = target.x < origin.x ? -1 : 1;
        int dz = target.z < origin.z ? -1 : 1;
        int depth = alongX ? bounds.Width : bounds.Height;
        int width = alongX ? bounds.Height : bounds.Width;
        for (int row = 0; row < depth; row++)
        {
            for (int col = 0; col < width; col++)
            {
                int offset = (row & 1) == 0 ? col : width - 1 - col;
                buffer.Add(new IntVec3(origin.x + dx * (alongX ? row : offset), 0,
                                       origin.z + dz * (alongX ? offset : row)));
            }
        }
    }
}

public sealed class CircleBrush : DrawStyle
{
    public override bool CanHaveDuplicates => false;

    public override void Update(IntVec3 origin, IntVec3 target, List<IntVec3> buffer)
    {
        Map map = Find.CurrentMap;
        if (map == null || !origin.InBounds(map)) return;
        int squared = origin.DistanceToSquared(target);
        int radius = (int)Math.Ceiling(Math.Sqrt(squared));
        CellRect bounds = CellRect.CenteredOn(origin, radius).ClipInsideMap(map);
        foreach (IntVec3 cell in bounds)
            if (cell.DistanceToSquared(origin) <= squared)
                buffer.Add(cell);
        // Keep drag rendering linear. Only the release needs centre-out ordering.
        if (Find.DesignatorManager.SelectedDesignator is BarrageDesignator brush)
            brush.Origin = origin;
    }
}
