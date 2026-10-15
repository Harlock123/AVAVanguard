namespace AVAVanguard.Engine;

/// <summary>
/// The computer pilot, for the demonstration (and the tests). It keeps to the safe band of the passage just
/// ahead, slips up or down out of the way of anything about to hit it, flies into charge pods, lines up level
/// with what's ahead, and fires whichever of its four guns points at something. At the Warden it hangs level
/// with the eye and fires through the gaps in the ring.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        var s = g.Ship;
        if (!s.Alive) return default;
        double x = g.ShipX;
        // The band it can safely be in over the next stretch of passage.
        double lo = double.MinValue, hi = double.MaxValue;
        // Looking further ahead the quicker the cave is going by.
        double reach = 120 + (GameEngine.ScrollSpeed * g.Pace + Math.Max(0, s.Sx - 420) * 0.6) * 0.9;
        for (double d = -Ship.HalfWidth - 4; d <= reach; d += 10)
        {
            var (c, f) = Field.Walls(x + d);
            lo = Math.Max(lo, c);
            hi = Math.Min(hi, f);
        }
        double top = lo + Ship.HalfHeight + 10, bottom = hi - Ship.HalfHeight - 10;
        if (top > bottom) top = bottom = (lo + hi) / 2;
        // Further on, where it'll want to be.
        var (fc, ff) = Field.Walls(x + 280);
        double ahead = Math.Clamp((fc + ff) / 2, top, bottom);

        if (g.Phase != Phase.Play)
        {
            Doing = "wait";
            return new Controls(Math.Sign(200 - s.Sx), Toward(s.Y, Math.Clamp(ahead, top, bottom)));
        }

        double wantY = s.Y, wantSx = 220;
        string doing = "cruise";
        // Not too near either wall.
        if (s.Y < top || s.Y > bottom) wantY = (top + bottom) / 2;
        var pod = g.Pods.Where(p => p.X > x - 20 && p.X - x < 700).MinBy(p => p.X);
        Enemy? target = null;
        if (g.InChamber && !g.Warden.Dead)
        {
            doing = "warden";
            wantY = g.Warden.Y;
            wantSx = 330;
        }
        else if (pod != null && s.Surge <= 0)
        {
            doing = "pod";
            wantY = pod.Y;
            wantSx = Math.Min(GameEngine.MaxSx - 20, s.Sx + (pod.X - x));
        }
        else
        {
            // The nearest thing ahead that can be lined up with; while charged, ram it.
            target = g.Enemies.Where(e => e.X > x + 30 && e.X - x < 800 && e.Kind != EnemyKind.Turret && e.Kind != EnemyKind.Rocket).MinBy(e => e.X - x + Math.Abs(e.Y - s.Y));
            if (target != null)
            {
                doing = s.Surge > 0 ? "ram" : "attack";
                wantY = target.Y + target.Vy * Math.Min(0.3, (target.X - x) / GameEngine.ShotSpeed);
                if (s.Surge > 0) wantSx = Math.Min(GameEngine.MaxSx - 20, s.Sx + (target.X - x));
            }
        }
        wantY = Math.Clamp(wantY, top, bottom);
        int dy = Toward(s.Y, wantY);

        // Anything about to hit it: up or down out of the way, to whichever side has room.
        if (s.Surge <= 0 && s.Safe <= 0.2)
        {
            double soonest = 99, threatY = 0;
            void Threat(double tx, double ty, double vx, double vy, double r)
            {
                for (double t = 0; t <= 0.7; t += 0.05)
                {
                    double ex = tx + vx * t, ey = ty + vy * t, mx = x + g.Pace * GameEngine.ScrollSpeed * t;
                    if (Math.Abs(ex - mx) < Ship.HalfWidth + r + 6 && Math.Abs(ey - s.Y) < Ship.HalfHeight + r + 6)
                    {
                        if (t < soonest) { soonest = t; threatY = ey; }
                        return;
                    }
                }
            }
            foreach (var b in g.Bolts) Threat(b.X, b.Y, b.Vx, b.Vy, 3);
            foreach (var e in g.Enemies) Threat(e.X, e.Y, e.Vx, e.Kind == EnemyKind.Rocket && !e.Going ? 0 : e.Vy, e.Radius * 0.75);
            if (soonest < 0.6)
            {
                doing = "evade";
                bool upRoom = s.Y - top > 40, downRoom = bottom - s.Y > 40;
                dy = threatY > s.Y ? (upRoom ? -1 : 1) : (downRoom ? 1 : -1);
            }
        }
        // Never into a wall.
        if (dy < 0 && s.Y <= top) dy = 0;
        if (dy > 0 && s.Y >= bottom) dy = 0;
        int dx = Math.Abs(wantSx - s.Sx) > 12 ? Math.Sign(wantSx - s.Sx) : 0;
        // Only forward if the passage further on has room at this height, and only back if the passage behind has.
        if (dx != 0)
            for (double d = dx > 0 ? reach : -260; d <= (dx > 0 ? reach + 300 : 0); d += 10)
            {
                var (c, f) = Field.Walls(x + d);
                if (s.Y - Ship.HalfHeight - 8 < c || s.Y + Ship.HalfHeight + 8 > f) { dx = 0; break; }
            }
        Doing = doing;

        // The guns: ahead or behind at anything level, up or down at anything straight above or below.
        int ax = 0, ay = 0;
        if (g.InChamber && !g.Warden.Dead && Math.Abs(g.Warden.Y - s.Y) < Warden.CoreRadius) ax = 1;
        foreach (var e in g.Enemies.Where(e => g.OnScreen(e.X, -10)).OrderBy(e => Math.Abs(e.X - x)))
        {
            if (Math.Abs(e.Y - s.Y) < e.Radius + 4) { ax = Math.Sign(e.X - x); break; }
            double lead = Math.Abs(e.Y - s.Y) / GameEngine.DropSpeed;
            double ex = e.X + (e.Vx - g.Pace * GameEngine.ScrollSpeed) * lead;
            if (Math.Abs(ex - x) < e.Radius + 6) { ay = Math.Sign(e.Y - s.Y); break; }
        }
        return new Controls(dx, dy, ax, ax != 0 ? 0 : ay);
    }

    private static int Toward(double from, double to) => Math.Abs(to - from) > 5 ? Math.Sign(to - from) : 0;
}
