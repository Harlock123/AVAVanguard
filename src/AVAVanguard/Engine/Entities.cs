namespace AVAVanguard.Engine;

/// <summary>Fly (Dx, Dy), and fire the guns: AimX -1/+1 for back or ahead, AimY -1/+1 for up or down.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, int AimX = 0, int AimY = 0)
{
    public bool Firing => AimX != 0 || AimY != 0;
}

/// <summary>The five zones of the cave, in the order they come.</summary>
public enum Zone { Spire, Band, Barren, Prism, Citadel }

/// <summary>
/// The cave: a long passage the screen scrolls along, its ceiling and floor different in every zone. The
/// shape comes from the place alone, so it's the same every time round.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The top of the play area, under the score bar.</summary>
    public const double Top = 92;
    public const double ZoneLength = 6000;
    /// <summary>Where the Citadel's passage ends and the Warden's chamber begins: the scroll stops there.</summary>
    public const double ChamberAt = 4 * ZoneLength + 4200;
    /// <summary>The narrowest the passage ever gets.</summary>
    public const double MinGap = 230;

    public static Zone ZoneAt(double x) => (Zone)Math.Clamp((int)(x / ZoneLength), 0, 4);

    public static string NameOf(Zone z) => z switch
    {
        Zone.Spire => "SPIRE ZONE",
        Zone.Band => "BAND ZONE",
        Zone.Barren => "BARREN ZONE",
        Zone.Prism => "PRISM ZONE",
        _ => "THE CITADEL",
    };

    /// <summary>Smooth noise: random heights at knots a fixed distance apart, eased between.</summary>
    private static double Noise(double x, double spacing, int salt)
    {
        double k = x / spacing;
        int i = (int)Math.Floor(k);
        double f = k - i, e = (1 - Math.Cos(f * Math.PI)) / 2;
        return Knot(i, salt) * (1 - e) + Knot(i + 1, salt) * e;
    }

    private static double Knot(int i, int salt)
    {
        uint h = (uint)(i * 73856093) ^ (uint)(salt * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h % 10000) / 10000.0 * 2 - 1;
    }

    /// <summary>The shape of one zone's walls at a place: the ceiling and the floor, before the zones are blended.</summary>
    private static (double Ceiling, double Floor) Shape(Zone z, double x)
    {
        switch (z)
        {
            case Zone.Spire:
            {
                // Open sky over sharp peaks.
                double peaks = Math.Pow(1 - Math.Abs(Noise(x, 220, 1)), 2) * 210 + 20 + Noise(x, 600, 2) * 30;
                return (Top, Height - 60 - peaks);
            }
            case Zone.Band:
            {
                // A smooth tube winding up and down.
                double mid = 420 + Noise(x, 700, 3) * 150, half = 150 + Noise(x, 500, 4) * 30;
                return (mid - half, mid + half);
            }
            case Zone.Barren:
            {
                // Winding, with jagged walls.
                double mid = 420 + Noise(x, 450, 5) * 140, half = 145 + Noise(x, 300, 6) * 25;
                return (mid - half + Noise(x, 60, 7) * 16, mid + half + Noise(x, 55, 8) * 16);
            }
            case Zone.Prism:
                return (Top, Height - 70 + Noise(x, 400, 9) * 30);
            default:
            {
                // Blocky walls, like masonry: the noise held flat between steps.
                double step = Math.Floor(x / 160) * 160;
                double mid = 420 + Noise(step, 520, 10) * 150, half = 135 + Noise(step, 640, 11) * 25;
                return (mid - half, mid + half);
            }
        }
    }

    /// <summary>The ceiling and floor at a place in the cave, eased from one zone into the next, never narrower than <see cref="MinGap"/>.</summary>
    public static (double Ceiling, double Floor) Walls(double x)
    {
        x = Math.Max(0, x);
        var z = ZoneAt(x);
        var (c, f) = Shape(z, x);
        double into = x - (int)z * ZoneLength;
        // The first stretch of a zone blends in from the last one.
        if (z != Zone.Spire && into < 500)
        {
            var (pc, pf) = Shape(z - 1, x);
            double t = into / 500;
            c = pc * (1 - t) + c * t;
            f = pf * (1 - t) + f * t;
        }
        // The Warden's chamber is wide open.
        if (x > ChamberAt - 400)
        {
            double t = Math.Clamp((x - (ChamberAt - 400)) / 400, 0, 1);
            c = c * (1 - t) + (Top + 30) * t;
            f = f * (1 - t) + (Height - 40) * t;
        }
        if (f - c < MinGap)
        {
            double mid = (c + f) / 2;
            c = mid - MinGap / 2;
            f = mid + MinGap / 2;
        }
        return (c, f);
    }

    public static double Ceiling(double x) => Walls(x).Ceiling;
    public static double Floor(double x) => Walls(x).Floor;
}

public sealed class Ship
{
    public const double HalfWidth = 20, HalfHeight = 9;
    /// <summary>Where on the screen it is (it flies anywhere on the left part of it), and its height.</summary>
    public double Sx = 200, Y = 420;
    public bool Alive = true;
    public double Fuel = 100, Surge, FireCool, Safe;
    /// <summary>How many it has rammed this surge: each is worth twice the last.</summary>
    public int Rams;
}

/// <summary>One of the ship's shots: a long streak ahead or behind, a short bolt up or down.</summary>
public sealed class Shot
{
    public double X, Y, Vx, Vy, Life;
    public bool Dead;
}

/// <summary>Something the enemy has fired.</summary>
public sealed class Bolt
{
    public double X, Y, Vx, Vy, Life;
    public bool Dead;
}

public enum EnemyKind
{
    /// <summary>Comes in from ahead in a line, weaving.</summary>
    Darter,
    /// <summary>Sits on the floor till the ship comes near, then launches.</summary>
    Rocket,
    /// <summary>Bounces from wall to wall as it drifts back.</summary>
    Coiler,
    /// <summary>Hangs in the passage, bobbing.</summary>
    Mine,
    /// <summary>Fixed to the ceiling or floor, firing at the ship.</summary>
    Turret,
    /// <summary>Hunts the ship, coming in from behind.</summary>
    Harrier,
    /// <summary>One link of a snaking chain.</summary>
    Swarmer,
}

public sealed class Enemy
{
    public EnemyKind Kind;
    /// <summary>A place in the cave, not on the screen.</summary>
    public double X, Y, Vx, Vy, T, FireIn = 2, Base;
    /// <summary>A Turret on the ceiling (rather than the floor); a Rocket that has launched.</summary>
    public bool Up, Going, Dead;
    /// <summary>A Swarmer's place in its chain (0 leads), and the chain it belongs to.</summary>
    public int Link, Chain;

    public double Radius => Kind switch
    {
        EnemyKind.Mine => 11,
        EnemyKind.Swarmer => 11,
        EnemyKind.Rocket => 12,
        EnemyKind.Turret => 16,
        _ => 15,
    };
}

/// <summary>A charge to fly into: while it lasts the ship can't be hurt by anything but the walls, and destroys whatever it touches.</summary>
public sealed class Pod
{
    public double X, Y, T;
}

/// <summary>The Warden: a great eye at the end of the Citadel, behind a turning ring of shield blocks.</summary>
public sealed class Warden
{
    public const int Blocks = 16, BlockHits = 3, CoreHits = 14;
    public const double Ring = 120, CoreRadius = 36;
    public double X = Field.ChamberAt + 800, Y = 440, Angle, T, FireIn = 2, Hurt, LaunchIn = 5;
    public int Hp = CoreHits;
    /// <summary>The hits each shield block can still take: 0 is a gap.</summary>
    public int[] Shield = new int[Blocks];
    public bool Dead;
}

/// <summary>A fleck of something blown up, flying out.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    /// <summary>X is a place in the cave (Word effects are on the screen).</summary>
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
