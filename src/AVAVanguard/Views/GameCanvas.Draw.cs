using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAVanguard.Engine;
using Effect = AVAVanguard.Engine.Effect;

namespace AVAVanguard.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAVanguard/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;
    private const uint Gold = 0xFFFFD84A, Hull = 0xFFE8ECF8;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    /// <summary>The colours of a charged ship, and of the Prism zone.</summary>
    private static readonly uint[] Rainbow = [0xFFFF4A4A, 0xFFFFA04A, 0xFFFFF04A, 0xFF6AFF6A, 0xFF6AE0FF, 0xFFB06AFF, 0xFFFFFFFF];

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000006), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            using (dc.PushClip(new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top)))
            {
                DrawStars(dc);
                DrawCave(dc);
                foreach (var p in g.Pods.Where(p => g.OnScreen(p.X, 30))) DrawPod(dc, new Point(g.ScreenX(p.X), p.Y), 1);
                if (g.InChamber || g.Scroll > Field.ChamberAt - Field.Width - 200) DrawWarden(dc);
                foreach (var e in g.Enemies.Where(e => g.OnScreen(e.X, 40)))
                    DrawEnemy(dc, e.Kind, new Point(g.ScreenX(e.X), e.Y), e.T, e.Up, e.Going, 1);
                foreach (var b in g.Bolts)
                {
                    var p = new Point(g.ScreenX(b.X), b.Y);
                    uint c = (int)(_clock * 20 + b.X) % 2 == 0 ? 0xFFFFFFFF : 0xFFFF6A6A;
                    dc.DrawEllipse(Brush(Alpha(c, 0.3)), null, p, 6, 6);
                    dc.DrawEllipse(Brush(c), null, p, 3, 3);
                }
                foreach (var s in g.Shots)
                {
                    var p = new Point(g.ScreenX(s.X), s.Y);
                    if (s.Vx != 0)
                    {
                        dc.DrawLine(Pen(Alpha(Yellow, 0.4), 5), p, new Point(p.X - Math.Sign(s.Vx) * 46, p.Y));
                        dc.DrawLine(Pen(0xFFFFFFE0, 2.2), p, new Point(p.X - Math.Sign(s.Vx) * 46, p.Y));
                    }
                    else
                    {
                        dc.DrawLine(Pen(Alpha(Cyan, 0.4), 6), p, new Point(p.X, p.Y - Math.Sign(s.Vy) * 18));
                        dc.DrawLine(Pen(0xFFE0FFFF, 2.5), p, new Point(p.X, p.Y - Math.Sign(s.Vy) * 18));
                    }
                }
                foreach (var d in g.Wreckage)
                {
                    double a = Math.Clamp(1 - d.Age / d.Life, 0, 1);
                    dc.FillRectangle(Brush(Alpha(d.Colour, a)), new Rect(g.ScreenX(d.X) - 2, d.Y - 2, 4, 4));
                }
                var ship = g.Ship;
                if (ship.Alive && (ship.Safe <= 0 || (int)(_clock * 10) % 2 == 0))
                    DrawShip(dc, new Point(ship.Sx, ship.Y), ship.Surge, 1);
                foreach (var e in g.Effects.Where(e => e.Kind != EffectKind.Word)) DrawEffect(dc, e, g.ScreenX(e.X));
            }
        }
        foreach (var e in g.Effects.Where(e => e.Kind == EffectKind.Word)) DrawEffect(dc, e, e.X);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(512, 380), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 14), 24, Yellow, center: true);
    }

    // ───────────────────────────── the cave ─────────────────────────────

    private void DrawStars(DrawingContext dc)
    {
        double cam = Engine.Scroll;
        for (int k = 0; k < 110; k++)
        {
            double depth = 0.05 + k % 3 * 0.08;
            double x = ((Hash(k, 1, 9) % 4096 - cam * depth) % 1024 + 1024) % 1024;
            double y = Field.Top + 6 + Hash(k, 2, 9) % (uint)(Field.Height - Field.Top - 10);
            double tw = 0.5 + 0.5 * Math.Sin(_clock * (0.8 + k % 5 * 0.5) + k);
            uint col = (k % 6) switch { 0 => 0xFFFFC0E0, 1 => 0xFFA0E0FF, 2 => 0xFFFFF0A0, _ => 0xFFFFFFFF };
            dc.FillRectangle(Brush(Alpha(col, 0.15 + 0.5 * tw)), new Rect(x, y, 2, 2));
        }
    }

    /// <summary>The colours of each zone's rock: its fill and its edge.</summary>
    private static (uint Fill, uint Edge) Rock(Zone z) => z switch
    {
        Zone.Spire => (0xFF2A0A3A, 0xFFE04AFF),
        Zone.Band => (0xFF0A2A3A, 0xFF4AE0FF),
        Zone.Barren => (0xFF2A2018, 0xFFB08A5A),
        Zone.Prism => (0xFF1A0A2A, 0xFFFFFFFF),
        _ => (0xFF3A1010, 0xFFFF6A4A),
    };

    /// <summary>The ceiling and the floor, drawn in short strips, each in the colours of its zone.</summary>
    private void DrawCave(DrawingContext dc)
    {
        double cam = Engine.Scroll;
        const double step = 8;
        double start = Math.Floor(cam / step) * step;
        for (double wx = start; wx < cam + Field.Width + step; wx += step)
        {
            var (c0, f0) = Field.Walls(wx);
            var (c1, f1) = Field.Walls(wx + step);
            double x0 = wx - cam, x1 = x0 + step;
            var zone = Field.ZoneAt(wx);
            var (fill, edge) = Rock(zone);
            if (c0 > Field.Top + 0.5 || c1 > Field.Top + 0.5)
            {
                Poly(dc, fill, null, 0, new Point(x0, Field.Top), new Point(x1 + 0.5, Field.Top), new Point(x1 + 0.5, c1), new Point(x0, c0));
                Stripes(dc, zone, wx, x0, x1, c0, c1, -1);
                dc.DrawLine(Pen(edge, 2.5), new Point(x0, c0), new Point(x1, c1));
            }
            Poly(dc, fill, null, 0, new Point(x0, f0), new Point(x1, f1), new Point(x1 + 0.5, Field.Height), new Point(x0, Field.Height));
            Stripes(dc, zone, wx, x0, x1, f0, f1, 1);
            dc.DrawLine(Pen(zone == Zone.Prism ? Rainbow[(int)(wx / 64) % 6] : edge, 2.5), new Point(x0, f0), new Point(x1, f1));
        }
    }

    /// <summary>Each zone's markings inside the rock: bands, strata, rainbow layers or masonry.</summary>
    private void Stripes(DrawingContext dc, Zone zone, double wx, double x0, double x1, double y0, double y1, int into)
    {
        switch (zone)
        {
            case Zone.Band:
                for (int i = 1; i <= 5; i++)
                {
                    uint c = i % 2 == 0 ? 0xFF2A8AC0 : 0xFF1A4A8A;
                    dc.DrawLine(Pen(c, 4), new Point(x0, y0 + into * i * 14), new Point(x1, y1 + into * i * 14));
                }
                break;
            case Zone.Prism:
                if (into > 0)
                    for (int i = 1; i <= 6; i++)
                        dc.DrawLine(Pen(Alpha(Rainbow[(i + (int)(_clock * 4)) % 6], 0.7), 4), new Point(x0, y0 + i * 9), new Point(x1, y1 + i * 9));
                break;
            case Zone.Citadel:
                for (int i = 1; i <= 6; i++)
                {
                    double y = (into > 0 ? y0 : y0) + into * i * 22;
                    dc.DrawLine(Pen(0xFF6A2018, 1.5), new Point(x0, y), new Point(x1, y));
                    if (((int)(wx / 8) + i * 3) % 6 == 0) dc.DrawLine(Pen(0xFF6A2018, 1.5), new Point(x0, y), new Point(x0, y - into * 22));
                }
                break;
            case Zone.Barren:
                if (Hash((int)(wx / 8), 3, 4) % 5 == 0)
                    dc.FillRectangle(Brush(0xFF4A3A2A), new Rect(x0, (y0 + y1) / 2 + into * (10 + Hash((int)wx, 1, 2) % 40), 4, 4));
                break;
            case Zone.Spire:
                dc.DrawLine(Pen(0xFF5A1A6A, 1.5), new Point(x0, y0 + into * 18), new Point(x1, y1 + into * 18));
                break;
        }
    }

    // ───────────────────────────── the ship ─────────────────────────────

    /// <summary>The ship: a white fighter facing ahead, with a gun pointing each way; charged, it blazes in shifting colours.</summary>
    private void DrawShip(DrawingContext dc, Point at, double surge, double k)
    {
        Point P(double x, double y) => new(at.X + x * k, at.Y + y * k);
        bool charged = surge > 0 && (surge > 1.5 || (int)(_clock * 8) % 2 == 0);
        uint body = charged ? Rainbow[(int)(_clock * 16) % 6] : Hull;
        if (charged) dc.DrawEllipse(Brush(Alpha(body, 0.25)), null, at, 34 * k, 22 * k);
        // The engine's glow.
        double f = 6 + 4 * Math.Sin(_clock * 40);
        Poly(dc, Alpha(Fire, 0.9), null, 0, P(-20, -4), P(-20 - f, 0), P(-20, 4));
        // The guns: ahead, behind, up and down.
        dc.FillRectangle(Brush(0xFF9AA0B8), new Rect(at.X - 2 * k, at.Y - 15 * k, 4 * k, 6 * k));
        dc.FillRectangle(Brush(0xFF9AA0B8), new Rect(at.X - 2 * k, at.Y + 9 * k, 4 * k, 6 * k));
        Poly(dc, body, 0xFF5A6080, 1, P(22, 1), P(10, -6), P(-8, -9), P(-20, -9), P(-20, 9), P(-8, 9), P(10, 6));
        Poly(dc, 0xFFE02A4A, null, 0, P(-18, -9), P(-10, -9), P(-16, -15), P(-22, -15));
        Poly(dc, 0xFFE02A4A, null, 0, P(-18, 9), P(-10, 9), P(-16, 15), P(-22, 15));
        dc.DrawEllipse(Brush(0xFF2A8AFF), null, P(4, -1), 6 * k, 3 * k);
    }

    /// <summary>A charge pod: a turning yellow hexagon with a bolt of lightning in it.</summary>
    private void DrawPod(DrawingContext dc, Point at, double k)
    {
        double a = _clock * 2;
        var hex = Enumerable.Range(0, 6).Select(i => new Point(at.X + Math.Cos(a + i * Math.PI / 3) * 16 * k, at.Y + Math.Sin(a + i * Math.PI / 3) * 16 * k)).ToArray();
        dc.DrawEllipse(Brush(Alpha(Yellow, 0.18 + 0.1 * Math.Sin(_clock * 6))), null, at, 26 * k, 26 * k);
        Poly(dc, 0xFF5A4A0A, Yellow, 2, hex);
        Poly(dc, 0xFFFFFFFF, null, 0, new Point(at.X + 2 * k, at.Y - 11 * k), new Point(at.X - 6 * k, at.Y + 2 * k), new Point(at.X, at.Y + 2 * k),
            new Point(at.X - 2 * k, at.Y + 11 * k), new Point(at.X + 6 * k, at.Y - 2 * k), new Point(at.X, at.Y - 2 * k));
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    private void DrawEnemy(DrawingContext dc, EnemyKind kind, Point at, double t, bool up, bool going, double k)
    {
        Point P(double x, double y) => new(at.X + x * k, at.Y + y * k);
        uint tint = GameEngine.Tint(kind);
        switch (kind)
        {
            case EnemyKind.Darter:
                // A swept dart, nose to the left, its tail flickering.
                Poly(dc, tint, 0xFFFFFFFF, 1, P(-16, 0), P(6, -10), P(14, -10), P(8, 0), P(14, 10), P(6, 10));
                dc.FillRectangle(Brush((int)(t * 12) % 2 == 0 ? Fire : Yellow), new Rect(at.X + 12 * k, at.Y - 2 * k, 6 * k, 4 * k));
                break;
            case EnemyKind.Rocket:
                Poly(dc, 0xFFE8E8F0, tint, 1.5, P(0, -16), P(6, -6), P(6, 10), P(-6, 10), P(-6, -6));
                Poly(dc, tint, null, 0, P(-6, 4), P(-11, 13), P(-6, 10));
                Poly(dc, tint, null, 0, P(6, 4), P(11, 13), P(6, 10));
                if (going)
                {
                    double f = 10 + 6 * Math.Sin(t * 40);
                    Poly(dc, Fire, null, 0, P(-4, 11), P(0, 11 + f), P(4, 11));
                }
                break;
            case EnemyKind.Coiler:
            {
                // A coiled spring, stretching and squashing.
                double sq = 1 + 0.25 * Math.Sin(t * 10);
                for (int i = 0; i < 4; i++)
                {
                    double y = (-12 + i * 8) * sq;
                    dc.DrawEllipse(null, Pen(tint, 3), P(0, y), 13 * k, 4 * k);
                }
                break;
            }
            case EnemyKind.Mine:
            {
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4 + t;
                    dc.DrawLine(Pen(tint, 2), P(Math.Cos(a) * 7, Math.Sin(a) * 7), P(Math.Cos(a) * 13, Math.Sin(a) * 13));
                }
                dc.DrawEllipse(Brush(0xFF4A1A3A), Pen(tint, 2), at, 8 * k, 8 * k);
                dc.DrawEllipse(Brush((int)(t * 4) % 2 == 0 ? Red : 0xFF6A0A0A), null, at, 3 * k, 3 * k);
                break;
            }
            case EnemyKind.Turret:
            {
                // A dome on the rock, its barrel tracking the ship.
                int s = up ? -1 : 1;
                var g = Engine;
                double aim = Math.Atan2(g.Ship.Y - at.Y, g.Ship.Sx - at.X);
                dc.DrawLine(Pen(0xFFC0C0C8, 5 * k), at, new Point(at.X + Math.Cos(aim) * 22 * k, at.Y + Math.Sin(aim) * 22 * k));
                Poly(dc, 0xFF1A4A1A, tint, 2, P(-16, 14 * s), P(-12, 0), P(-6, -6 * s), P(6, -6 * s), P(12, 0), P(16, 14 * s));
                dc.DrawEllipse(Brush(Yellow), null, at, 3 * k, 3 * k);
                break;
            }
            case EnemyKind.Harrier:
            {
                // A hawk-like raider, wings beating.
                double flap = Math.Sin(t * 14) * 8;
                Poly(dc, tint, 0xFF5A2A0A, 1.2, P(16, 0), P(-4, -6), P(-14, -14 - flap), P(-8, 0), P(-14, 14 + flap), P(-4, 6));
                dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(at.X + 6 * k, at.Y - 2 * k, 3 * k, 3 * k));
                break;
            }
            default:
                // A Swarmer: a glowing bead of a chain.
                dc.DrawEllipse(Brush(Alpha(tint, 0.35)), null, at, 13 * k, 13 * k);
                dc.DrawEllipse(Brush(tint), Pen(0xFFFFFFFF, 1.2), at, 8 * k, 8 * k);
                break;
        }
    }

    /// <summary>The Warden: a great eye that follows the ship, behind its turning ring of shield blocks.</summary>
    private void DrawWarden(DrawingContext dc)
    {
        var g = Engine;
        var w = g.Warden;
        if (w.Dead) return;
        var at = new Point(g.ScreenX(w.X), w.Y);
        // The chamber's back wall: its machinery.
        for (int i = 0; i < 6; i++)
            dc.DrawLine(Pen(0xFF3A1A2A, 6), new Point(at.X + 150 + i * 14, Field.Top), new Point(at.X + 150 + i * 14, Field.Height));
        dc.DrawLine(Pen(Alpha(0xFFFF4A6A, 0.5), 4), new Point(at.X + 60, at.Y), new Point(at.X + 150, at.Y));
        // The ring.
        double seg = Math.PI * 2 / Warden.Blocks;
        for (int i = 0; i < Warden.Blocks; i++)
        {
            int hp = w.Shield[i];
            if (hp <= 0) continue;
            double a0 = w.Angle + i * seg + 0.05, a1 = w.Angle + (i + 1) * seg - 0.05;
            uint c = hp switch { 3 => 0xFF4AA0FF, 2 => 0xFFB06AFF, _ => 0xFFFF4A8A };
            Point R(double a, double r) => new(at.X + Math.Cos(a) * r, at.Y + Math.Sin(a) * r);
            Poly(dc, Alpha(c, 0.75), 0xFFFFFFFF, 1, R(a0, Warden.Ring - 10), R(a1, Warden.Ring - 10), R(a1, Warden.Ring + 10), R(a0, Warden.Ring + 10));
        }
        // The eye.
        uint white = w.Hurt > 0 ? 0xFFFF6A6A : 0xFFF4E8E8;
        dc.DrawEllipse(Brush(Alpha(Red, 0.25)), null, at, Warden.CoreRadius + 12, Warden.CoreRadius + 12);
        dc.DrawEllipse(Brush(white), Pen(0xFF8A1A2A, 3), at, Warden.CoreRadius, Warden.CoreRadius * 0.8);
        double look = Math.Atan2(g.Ship.Y - at.Y, g.Ship.Sx - at.X);
        var iris = new Point(at.X + Math.Cos(look) * 14, at.Y + Math.Sin(look) * 10);
        dc.DrawEllipse(Brush(0xFFE02A2A), null, iris, 15, 15);
        dc.DrawEllipse(Brush(0xFF100000), null, iris, 6, 9);
        // How much it has left.
        for (int i = 0; i < Warden.CoreHits; i++)
            dc.FillRectangle(Brush(i < w.Hp ? Red : 0xFF3A1010), new Rect(at.X - 70 + i * 10, at.Y + Warden.Ring + 24, 8, 6));
    }

    private void DrawEffect(DrawingContext dc, Effect e, double x)
    {
        double t = e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (8 + 50 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 3), new Point(x, e.Y), r, r * 0.85);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(x, e.Y - t * 24), 26, Alpha(e.Colour, 1 - t * t), center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(x, e.Y - t * 30), 32, Alpha(e.Colour, 1 - t * t), center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 44), 36, Yellow);
        Text(dc, "HI", new Point(1008, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(1008, 44), 30, Dim, right: true);
        if (!InGame) return;
        int spare = Math.Max(0, g.Lives - (g.Ship.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawShip(dc, new Point(30 + i * 36, 76), 0, 0.55);
        // Fuel.
        Text(dc, "FUEL", new Point(210, 22), 22, Dim, right: true);
        dc.DrawRectangle(Brush(0xFF101830), Pen(Dim, 1), new Rect(220, 14, 200, 16));
        double fuel = g.Ship.Fuel;
        uint fc = fuel < GameEngine.LowFuel ? ((int)(_clock * 4) % 2 == 0 ? Red : 0xFF6A1A1A) : fuel < 45 ? Yellow : 0xFF6AFF8A;
        dc.FillRectangle(Brush(fc), new Rect(222, 16, 196 * fuel / 100, 12));
        if (g.Ship.Surge > 0)
        {
            Text(dc, "SURGE", new Point(210, 48), 22, Rainbow[(int)(_clock * 12) % 6], right: true);
            dc.FillRectangle(Brush(Rainbow[(int)(_clock * 12) % 6]), new Rect(222, 42, 196 * g.Ship.Surge / GameEngine.SurgeSeconds, 10));
        }
        // The zones, and how far along them the ship has come.
        double left = 450, width = 400;
        for (int z = 0; z < 5; z++)
        {
            double x0 = left + z * width / 5, w = width / 5 - 4;
            var (fill, edge) = Rock((Zone)z);
            dc.FillRectangle(Brush(z == (int)g.Zone ? Alpha(edge, 0.5) : fill), new Rect(x0, 12, w, 18));
            dc.DrawRectangle(null, Pen(edge, 1), new Rect(x0, 12, w, 18));
            Text(dc, Field.NameOf((Zone)z).Replace(" ZONE", "").Replace("THE ", ""), new Point(x0 + w / 2, 44), 18, z == (int)g.Zone ? White : Dim, center: true);
        }
        double along = Math.Clamp(g.ShipX / (Field.ChamberAt + 800), 0, 1);
        double mx = left + along * width;
        Poly(dc, White, null, 0, new Point(mx, 34), new Point(mx - 5, 26), new Point(mx + 5, 26));
        Text(dc, $"ROUND {g.Round}", new Point(left + width / 2, 74), 22, Gold, center: true);
        dc.DrawLine(Pen(0xFF2A2A5A, 2), new Point(0, Field.Top - 2), new Point(1024, Field.Top - 2));
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = 512, cy = 260;
        var (w, h) = (Math.Max(msg.Length * 28, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xA0000010), new Rect(cx - w / 2, cy - 48, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 12 ? 46 : 58, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 28, White, center: true);
    }

    // ───────────────────────────── the screens ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = 512;
        // A strip of each zone's rock along the bottom.
        for (int z = 0; z < 5; z++)
        {
            var (fill, edge) = Rock((Zone)z);
            dc.FillRectangle(Brush(fill), new Rect(z * 204.8, 730, 204.8, 38));
            dc.DrawLine(Pen(edge, 3), new Point(z * 204.8, 730), new Point((z + 1) * 204.8, 730));
        }
        Text(dc, "AVA VANGUARD", new Point(cx, 130), 112, Gold, center: true);
        Text(dc, "AFTER THE 1981 ARCADE CLASSIC", new Point(cx, 196), 28, Dim, center: true);
        // The ship firing all four ways.
        var at = new Point(cx, 268);
        DrawShip(dc, at, (int)(_clock / 3) % 2 == 0 ? 0 : 5, 1.6);
        double r = 50 + (_clock * 260) % 160;
        dc.DrawLine(Pen(0xFFFFFFE0, 2.5), new Point(cx + r, at.Y), new Point(cx + r + 40, at.Y));
        dc.DrawLine(Pen(0xFFFFFFE0, 2.5), new Point(cx - r, at.Y), new Point(cx - r - 40, at.Y));
        double v = 30 + (_clock * 120) % 40;
        dc.DrawLine(Pen(0xFFE0FFFF, 3), new Point(cx, at.Y - v), new Point(cx, at.Y - v - 14));
        dc.DrawLine(Pen(0xFFE0FFFF, 3), new Point(cx, at.Y + v), new Point(cx, at.Y + v + 14));
        (EnemyKind Kind, string Name)[] roster =
        [
            (EnemyKind.Darter, "DARTER"), (EnemyKind.Rocket, "ROCKET"), (EnemyKind.Coiler, "COILER"), (EnemyKind.Mine, "MINE"),
            (EnemyKind.Turret, "TURRET"), (EnemyKind.Harrier, "HARRIER"), (EnemyKind.Swarmer, "SWARMER"),
        ];
        for (int i = 0; i < roster.Length; i++)
        {
            double x = cx - 390 + i * 130, y = 370;
            DrawEnemy(dc, roster[i].Kind, new Point(x, y), _clock + i, false, roster[i].Kind == EnemyKind.Rocket, 1.4);
            Text(dc, roster[i].Name, new Point(x, y + 36), 22, GameEngine.Tint(roster[i].Kind), center: true);
            Text(dc, GameEngine.Points(roster[i].Kind).ToString(), new Point(x, y + 58), 22, White, center: true);
        }
        DrawPod(dc, new Point(cx - 300, 480), 1.2);
        Text(dc, "FLY INTO A POD TO SURGE - RAM THEM ALL", new Point(cx + 20, 480), 24, Yellow, center: true);
        Text(dc, "DESTROY THINGS TO REFUEL - REACH THE CITADEL - DESTROY THE WARDEN", new Point(cx, 516), 22, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 580), 46, Yellow, center: true);
        Text(dc, "ARROWS: FLY    W A S D: FIRE UP, BACK, DOWN, AHEAD    SPACE: FIRE AHEAD", new Point(cx, 636), 23, Dim, center: true);
        Text(dc, $"EXTRA SHIP AT {GameEngine.FirstExtraLife} AND EVERY {GameEngine.ExtraLifeEvery}    P: PAUSE    M: SOUND", new Point(cx, 668), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE VANGUARD", new Point(cx, 160), 72, Gold, center: true);
        Text(dc, "RANK NAME    SCORE   ZONE", new Point(cx, 222), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 266 + i * 40), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 220), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 290), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 370), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 460), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 500, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 580), 26, White, center: true);
    }
}
