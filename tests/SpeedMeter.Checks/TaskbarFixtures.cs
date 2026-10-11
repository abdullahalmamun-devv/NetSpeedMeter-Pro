using System;
using SpeedMeter.Windows;

namespace SpeedMeter.Checks;

public static class TaskbarFixtures
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run(Action<string, Action> check)
    {
        var workArea = new TaskbarRect(0, 0, 1920, 1040);

        check("TASKBAR: Bottom taskbar positions docked companion above taskbar edge", () =>
        {
            var bottomBar = new TaskbarInfo(new TaskbarRect(0, 1040, 1920, 1080), TaskbarEdge.Bottom, false, true);
            var placement = TaskbarCompanion.CalculatePlacement(bottomBar, 200, 40, workArea);

            Require(!placement.UseFloatingFallback, "Docking must succeed for standard bottom taskbar");
            Require(placement.Y == 1040 - 40 - 4, $"Expected Y=996, got {placement.Y}");
            Require(placement.X == 1920 - 200 - 12, $"Expected X=1708, got {placement.X}");
        });

        check("TASKBAR: Top taskbar positions docked companion below taskbar edge", () =>
        {
            var topBar = new TaskbarInfo(new TaskbarRect(0, 0, 1920, 40), TaskbarEdge.Top, false, true);
            var placement = TaskbarCompanion.CalculatePlacement(topBar, 200, 40, workArea);

            Require(!placement.UseFloatingFallback, "Docking must succeed for top taskbar");
            Require(placement.Y == 44, $"Expected Y=44, got {placement.Y}");
        });

        check("TASKBAR: Left taskbar positions docked companion right of taskbar edge", () =>
        {
            var leftBar = new TaskbarInfo(new TaskbarRect(0, 0, 60, 1080), TaskbarEdge.Left, false, true);
            var placement = TaskbarCompanion.CalculatePlacement(leftBar, 200, 40, workArea);

            Require(!placement.UseFloatingFallback, "Docking must succeed for left taskbar");
            Require(placement.X == 64, $"Expected X=64, got {placement.X}");
        });

        check("TASKBAR: Right taskbar positions docked companion left of taskbar edge", () =>
        {
            var rightBar = new TaskbarInfo(new TaskbarRect(1860, 0, 1920, 1080), TaskbarEdge.Right, false, true);
            var placement = TaskbarCompanion.CalculatePlacement(rightBar, 200, 40, workArea);

            Require(!placement.UseFloatingFallback, "Docking must succeed for right taskbar");
            Require(placement.X == 1860 - 200 - 4, $"Expected X=1656, got {placement.X}");
        });

        check("TASKBAR: Auto-hide taskbar forces safe floating fallback", () =>
        {
            var autoHideBar = new TaskbarInfo(new TaskbarRect(0, 1078, 1920, 1080), TaskbarEdge.Bottom, true, true);
            var placement = TaskbarCompanion.CalculatePlacement(autoHideBar, 200, 40, workArea);

            Require(placement.UseFloatingFallback, "Auto-hide MUST trigger floating fallback to prevent shell occlusion");
        });

        check("TASKBAR: Invalid taskbar query forces safe floating fallback", () =>
        {
            var invalidBar = new TaskbarInfo(default, TaskbarEdge.Unknown, false, false);
            var placement = TaskbarCompanion.CalculatePlacement(invalidBar, 200, 40, workArea);

            Require(placement.UseFloatingFallback, "Invalid taskbar MUST trigger floating fallback");
        });
    }
}
