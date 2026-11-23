using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace BarrageOrders.GameTests;

internal static class GameInput
{
    private static readonly MethodInfo SetPosition = typeof(UnityEngine.InputSystem.Pointer)
        .GetMethod("SetPosition", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static void Deliver(IntVec3 cell, EventType type, int button = 0, Action inspect = null)
    {
        // Drive the game's own input buffer for one frame. The Windows cursor stays with the user.
        var pointer = UnityEngine.InputSystem.Mouse.current;
        var before = pointer.position.ReadValue();
        var previousEvent = Event.current;
        try
        {
            var point = (Vector2)Find.Camera.WorldToScreenPoint(cell.ToVector3Shifted());
            SetPosition.Invoke(pointer, new object[] { point });
            if (UI.MouseCell() != cell) throw new Exception($"Pointer mapped to {UI.MouseCell()} instead of {cell}.");
            Event.current = new Event { type = type, button = button };
            Find.DesignatorManager.ProcessInputEvents();
            inspect?.Invoke();
        }
        finally
        {
            Event.current = previousEvent;
            SetPosition.Invoke(pointer, new object[] { before });
        }
    }
}
