using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAVanguard.Engine;
using AVAVanguard.Views;
using Controls = AVAVanguard.Engine.Controls;

namespace AVAVanguard.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set VANGUARD_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avavanguard-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("VANGUARD_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAVanguard-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { AimX = 0, AimY = 0 } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }


    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 98250, 12);
        g.HighScores.Insert("AVA", 71800, 9);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    /// <summary>The pilot plays a zone until there's plenty going on.</summary>
    private static void Zone(int wave, string name, Func<GameEngine, bool> busy, int seed = 2, double after = 8)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, g => g.ZoneTime > after && g.Ship.Alive && g.Ship.Safe <= 0 && g.Phase == Phase.Play && busy(g), 120));
        Shot(w, c, name);
    }

    private static int Seen(GameEngine g, EnemyKind k) => g.Enemies.Count(e => e.Kind == k && g.OnScreen(e.X, -20));

    [AvaloniaFact]
    public void The_Spire_zone() => Zone(1, "spire", g => g.Zone == Engine.Zone.Spire && Seen(g, EnemyKind.Darter) >= 3 && g.Shots.Count > 0);

    [AvaloniaFact]
    public void The_Band_zone() => Zone(2, "band", g => g.Zone == Engine.Zone.Band && Seen(g, EnemyKind.Coiler) + Seen(g, EnemyKind.Mine) >= 4 && g.Shots.Count > 0);

    [AvaloniaFact]
    public void The_Barren_zone() => Zone(3, "barren", g => g.Zone == Engine.Zone.Barren && Seen(g, EnemyKind.Turret) >= 2 && g.Bolts.Count >= 2);

    [AvaloniaFact]
    public void The_Prism_zone() => Zone(4, "prism", g => g.Zone == Engine.Zone.Prism && Seen(g, EnemyKind.Swarmer) >= 5);

    [AvaloniaFact]
    public void The_Citadel() => Zone(5, "citadel", g => g.Enemies.Count(e => g.OnScreen(e.X, -20)) >= 3 && g.Shots.Count > 0, after: 20);

    [AvaloniaFact]
    public void A_surge() => Zone(2, "surge", g => g.Ship.Surge > 3 && g.Enemies.Count(e => g.OnScreen(e.X, -20)) >= 3, after: 2);

    [AvaloniaFact]
    public void The_Warden()
    {
        var (w, c, g) = Open(6);
        g.Go(5);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, g => g.InChamber && g.Warden.Hp <= Warden.CoreHits - 4 && g.Ship.Alive && g.Bolts.Count >= 2, 150));
        Shot(w, c, "warden");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Victory && g.PhaseTime > 0.5, 120));
        Shot(w, c, "victory");
    }

    [AvaloniaFact]
    public void A_ship_lost()
    {
        var (w, c, g) = Open(8);
        g.Go(3);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.ZoneTime > 6 && g.Ship.Alive && g.Ship.Safe <= 0 && g.Enemies.Count(e => g.OnScreen(e.X)) >= 2));
        g.PutBolt(g.ShipX, g.Ship.Y - 30, GameEngine.ScrollSpeed, 300);
        g.Idle(20);
        Assert.Equal(Phase.Dying, g.Phase);
        c.Advance(0.3);
        Shot(w, c, "lost");
    }
}
