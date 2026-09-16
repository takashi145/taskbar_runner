using System.Linq;
using System.Runtime.CompilerServices;
using TaskbarRunner.Core;

namespace TaskbarRunner.Tests;

internal static class AvoidancePilot
{
    private sealed class Flight
    {
        internal double StartedAt;
        internal Obstacle? Target;
    }

    private static readonly ConditionalWeakTable<GameSession, Flight> Flights = new();

    internal static void Steer(GameSession game, bool duckOverheadBars = true)
    {
        var flight = Flights.GetValue(game, _ => new Flight());
        var next = game.Obstacles.FirstOrDefault(o => o.X + o.Width > game.PlayerX + 7);
        var leadTime = next?.Kind switch
        {
            ObstacleKind.TallBar => .34,
            ObstacleKind.WideBar => .10,
            _ => .19
        };
        var approaching = next is not null &&
            next.X - (game.PlayerX + GameSession.PlayerWidth) < game.Speed * leadTime;
        game.Duck(approaching && next!.Kind == ObstacleKind.OverheadBar && duckOverheadBars);
        if (!approaching || next!.Kind == ObstacleKind.OverheadBar) return;
        if (game.IsGrounded)
        {
            if (game.Jump())
            {
                flight.StartedAt = game.Elapsed;
                flight.Target = next;
            }
        }
        else if (next == flight.Target && next.Kind is (ObstacleKind.TallBar or ObstacleKind.WideBar) && game.JumpsUsed == 1 &&
            game.Elapsed - flight.StartedAt >= (next.Kind == ObstacleKind.WideBar ? .40 : 4.0 / 15) - 1e-9)
        {
            game.Jump();
        }
    }
}
