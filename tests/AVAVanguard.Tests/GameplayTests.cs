using AVAVanguard.Engine;

namespace AVAVanguard.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avavanguard-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A zone in play with nothing in it, nothing coming and nothing firing unless a test makes it.</summary>
    private static GameEngine Game(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        return g;
    }

    /// <summary>A real game in play, with plenty of ships.</summary>
    private static GameEngine Real(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.SetLives(99);
        return g;
    }

    /// <summary>A Mine (it stays put) at a distance from the ship.</summary>
    private static Enemy MineAt(GameEngine g, double dx, double dy) => g.PutEnemy(EnemyKind.Mine, g.ShipX + dx, g.Ship.Y + dy);

    private static readonly Controls Ahead = new(AimX: 1);

    // ───────────────────────────── the ship ─────────────────────────────

    [Fact]
    public void The_ship_flies_about_the_left_of_the_screen()
    {
        var g = Game();
        g.Run(200, new Controls(Dx: 1));
        Assert.Equal(GameEngine.MaxSx, g.Ship.Sx);
        g.Run(200, new Controls(Dx: -1));
        Assert.Equal(GameEngine.MinSx, g.Ship.Sx);
        double y = g.Ship.Y;
        g.Run(10, new Controls(Dy: -1));
        Assert.Equal(y - GameEngine.FlySpeedY / 6, g.Ship.Y, 3);
    }

    [Fact]
    public void The_cave_moves_on_by_itself_and_quicker_when_the_ship_pushes_forward()
    {
        var g = Game();
        double s0 = g.Scroll;
        g.Idle(60);
        double slow = g.Scroll - s0;
        Assert.Equal(GameEngine.ScrollSpeed, slow, 1);
        g.SetShip(GameEngine.MaxSx, g.Ship.Y);
        s0 = g.Scroll;
        g.Idle(60);
        Assert.True(g.Scroll - s0 > slow * 1.8);
    }

    [Fact]
    public void Touching_the_rock_destroys_the_ship()
    {
        var g = Game(2);
        g.Jump(Field.ZoneLength + 2500);
        g.SetShip(200, Field.Ceiling(g.Scroll + 200) + 3);
        g.Tick(default);
        Assert.Equal("CRASHED", g.Cause);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 1)]
    public void Each_gun_fires_its_own_way_and_hits_what_is_there(int ax, int ay)
    {
        var g = Game();
        var target = MineAt(g, ax * 160, ay * 120);
        g.Tick(new Controls(AimX: ax, AimY: ay));
        var shot = Assert.Single(g.Shots);
        Assert.Equal(ax, Math.Sign(shot.Vx));
        Assert.Equal(ay, Math.Sign(shot.Vy));
        g.Idle(15);
        Assert.True(target.Dead);
        Assert.Equal(GameEngine.Points(EnemyKind.Mine), g.Score);
    }

    [Fact]
    public void Aiming_diagonally_fires_ahead_or_behind()
    {
        var g = Game();
        g.Tick(new Controls(AimX: -1, AimY: 1));
        var shot = Assert.Single(g.Shots);
        Assert.True(shot.Vx < 0 && shot.Vy == 0);
    }

    [Fact]
    public void Six_shots_at_most_and_the_rock_stops_them()
    {
        var g = Game();
        int most = 0;
        for (int t = 0; t < 120; t++)
        {
            g.Tick(new Controls(AimY: t % 2 == 0 ? -1 : 1, AimX: t % 3 == 0 ? 1 : 0));
            most = Math.Max(most, g.Shots.Count);
        }
        Assert.InRange(most, 3, 6);
        // In the Band zone the ceiling is close above: a shot up stops there.
        var b = Game(2);
        b.Jump(Field.ZoneLength + 2500);
        double gapAbove = b.Ship.Y - Field.Ceiling(b.ShipX);
        b.Tick(new Controls(AimY: -1));
        b.Idle((int)(gapAbove / GameEngine.DropSpeed * 60) + 3);
        Assert.Empty(b.Shots);
    }

    [Fact]
    public void Fuel_burns_destroying_things_puts_it_back_and_running_out_is_the_end()
    {
        var g = Game();
        g.Idle(300);
        Assert.Equal(100 - GameEngine.FuelBurn * 5, g.Ship.Fuel, 1);
        double fuel = g.Ship.Fuel;
        MineAt(g, 200, 0);
        g.Tick(Ahead);
        g.Idle(10);
        Assert.True(g.Ship.Fuel > fuel);
        g.Ship.Fuel = 0.01;
        g.Tick(default);
        Assert.Equal("OUT OF FUEL", g.Cause);
    }

    [Fact]
    public void A_pod_charges_the_ship_to_ram_for_doubling_points_until_it_wears_off()
    {
        var g = Game();
        g.PutPod(g.ShipX + 5, g.Ship.Y);
        g.Tick(default);
        Assert.True(g.Ship.Surge > 0);
        for (int i = 0; i < 3; i++)
        {
            MineAt(g, 0, 0);
            g.Tick(default);
        }
        Assert.Equal(100 + 200 + 400, g.Score);
        g.PutBolt(g.ShipX, g.Ship.Y);
        g.Tick(default);
        Assert.True(g.Ship.Alive, "shots can't hurt it");
        g.Idle((int)(GameEngine.SurgeSeconds * 60));
        Assert.Equal(0, g.Ship.Surge);
        MineAt(g, 0, 0);
        g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
    }

    [Fact]
    public void Charged_or_not_the_rock_still_destroys_the_ship()
    {
        var g = Game(2);
        g.Jump(Field.ZoneLength + 2500);
        g.PutPod(g.ShipX, g.Ship.Y);
        g.Tick(default);
        g.SetShip(200, Field.Floor(g.Scroll + 200) - 3);
        g.Tick(default);
        Assert.Equal("CRASHED", g.Cause);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    [Fact]
    public void A_Rocket_waits_on_the_floor_then_launches_and_is_worth_more_in_the_air()
    {
        var g = Game();
        g.Calm = false;
        double x = g.ShipX + 700;
        var r = g.PutEnemy(EnemyKind.Rocket, x, Field.Floor(x) - 14);
        g.Idle(60);
        Assert.False(r.Going);
        for (int t = 0; t < 400 && !r.Going; t++) g.Tick(default);
        Assert.True(r.Going);
        Assert.True(r.X - g.ShipX < 400);
        g.Idle(30);
        Assert.True(r.Y < Field.Floor(r.X) - 60);
        g.Calm = true;
        g.SetShip(r.X - g.Scroll - 200, r.Y + r.Vy * 0.2);
        g.Tick(Ahead);
        g.Idle(15);
        Assert.True(r.Dead);
        Assert.Equal(100, g.Score);
    }

    [Fact]
    public void A_Coiler_bounces_between_ceiling_and_floor()
    {
        var g = Game(2);
        g.Jump(Field.ZoneLength + 2500);
        var c = g.PutEnemy(EnemyKind.Coiler, g.ShipX + 600, g.Ship.Y);
        c.Vy = -220;
        double low = double.MaxValue, high = double.MinValue;
        for (int t = 0; t < 240; t++)
        {
            g.Tick(new Controls(Dy: 0));
            low = Math.Min(low, c.Y - Field.Ceiling(c.X));
            high = Math.Max(high, Field.Floor(c.X) - c.Y);
        }
        Assert.InRange(low, 15, 25);
        Assert.True(high > 150);
    }

    [Fact]
    public void Only_what_is_on_the_screen_fires()
    {
        var g = Game(3);
        g.Jump(2 * Field.ZoneLength + 2000);
        g.Calm = false;
        double x = g.ShipX + 1200;
        var far = g.PutEnemy(EnemyKind.Turret, x, Field.Floor(x) - 14);
        far.FireIn = 0.1;
        g.Run(20, new Controls(Dx: -1));
        Assert.Empty(g.Bolts);
        Assert.DoesNotContain(g.Enemies, e => e != far);
        g.Idle(260);
        Assert.NotEmpty(g.Bolts);
    }

    [Fact]
    public void A_Harrier_comes_from_behind_and_closes_in()
    {
        var g = Game();
        var h = g.PutEnemy(EnemyKind.Harrier, g.Scroll - 40, g.Ship.Y + 100);
        Assert.True(h.X < g.ShipX - 150);
        double closest = double.MaxValue;
        for (int t = 0; t < 90 && g.Ship.Alive; t++)
        {
            g.Tick(default);
            closest = Math.Min(closest, Math.Abs(h.X - g.ShipX) + Math.Abs(h.Y - g.Ship.Y));
        }
        Assert.True(closest < 60, $"closest {closest:0}");
    }

    [Fact]
    public void Swarmers_follow_the_one_in_front_along_its_path()
    {
        var g = Game(4);
        g.Jump(3 * Field.ZoneLength + 2000);
        var chain = Enumerable.Range(0, 3).Select(i =>
        {
            var e = g.PutEnemy(EnemyKind.Swarmer, g.ShipX + 600 + i * 26, 400);
            e.Link = i;
            return e;
        }).ToList();
        var path = new List<(double X, double Y)>();
        for (int t = 0; t < 120; t++)
        {
            g.Tick(default);
            path.Add((chain[0].X, chain[0].Y));
        }
        // The second is now where the first was a little while ago.
        var (px, py) = path.MinBy(p => Math.Abs(p.X - chain[1].X));
        Assert.InRange(chain[1].Y - py, -12, 12);
    }

    [Fact]
    public void Each_zone_sends_its_own_kinds()
    {
        var expected = new Dictionary<int, EnemyKind[]>
        {
            [1] = [EnemyKind.Darter, EnemyKind.Rocket],
            [2] = [EnemyKind.Coiler, EnemyKind.Mine, EnemyKind.Darter],
            [4] = [EnemyKind.Swarmer, EnemyKind.Darter, EnemyKind.Rocket],
        };
        foreach (var (wave, kinds) in expected)
        {
            var g = Real(wave);
            var seen = new HashSet<EnemyKind>();
            for (int t = 0; t < 60 * 30; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                foreach (var e in g.Enemies) seen.Add(e.Kind);
            }
            Assert.Subset(kinds.ToHashSet(), seen);
            Assert.True(seen.Count >= 2);
        }
    }

    // ───────────────────────────── zones and the Warden ─────────────────────────────

    [Fact]
    public void Into_the_next_zone_for_a_fuel_bonus()
    {
        var g = Game();
        g.Jump(Field.ZoneLength - 250);
        g.Ship.Fuel = 60;
        for (int t = 0; t < 600 && g.Wave == 1; t++) g.Tick(default);
        Assert.Equal(2, g.Wave);
        Assert.Equal(Zone.Band, g.Zone);
        Assert.InRange(g.Bonus, 540, 600);
        Assert.InRange(g.Ship.Fuel, 99, 100);
    }

    [Fact]
    public void The_cave_stops_at_the_Warden()
    {
        var g = Game(5);
        g.Jump(Field.ChamberAt - 100);
        g.Idle(120);
        Assert.Equal(Field.ChamberAt, g.Scroll);
        Assert.True(g.InChamber);
    }

    [Fact]
    public void The_Warden_shield_takes_three_hits_a_block_then_the_eye_falls_and_the_round_begins_again()
    {
        var g = Game(5);
        g.Jump(Field.ChamberAt);
        var w = g.Warden;
        // A shot straight at a block of the ring.
        g.SetShip(400, w.Y);
        int block = g.Block(Math.PI);
        if (w.Shield[block] == 0) w.Shield[block] = Warden.BlockHits;
        // One shot at a time, with the ring held still and the ship level with the eye.
        void Fire()
        {
            g.SetShip(400, w.Y);
            for (int t = 0; t < 30; t++)
            {
                w.Angle = 0;
                g.Tick(t == 0 ? Ahead : default);
            }
        }
        int fired = 0;
        while (w.Shield[block] > 0 && fired < 10)
        {
            Fire();
            fired++;
        }
        Assert.Equal(Warden.BlockHits, fired);
        // Through the gap now, into the eye.
        while (!w.Dead && fired < 40)
        {
            Fire();
            fired++;
        }
        Assert.True(w.Dead);
        Assert.Equal(Phase.Victory, g.Phase);
        Assert.Equal(GameEngine.WardenPoints, g.Bonus);
        g.Idle((int)(GameEngine.VictorySeconds * 60) + 5);
        Assert.Equal(6, g.Wave);
        Assert.Equal(2, g.Round);
        Assert.Equal(Zone.Spire, g.Zone);
        Assert.Equal(0, g.Scroll);
        Assert.False(g.Warden.Dead);
        Assert.True(g.Pace > 1);
    }

    // ───────────────────────────── losing ships ─────────────────────────────

    [Fact]
    public void A_lost_ship_goes_back_a_little_refuelled_and_the_last_ends_the_game()
    {
        var g = Game(2);
        g.SetLives(2);
        g.Jump(Field.ZoneLength + 3000);
        g.Ship.Fuel = 30;
        MineAt(g, 0, 0);
        g.Tick(default);
        Assert.Equal("HIT BY A MINE", g.Cause);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 5);
        Assert.InRange(g.Scroll, Field.ZoneLength + 2295, Field.ZoneLength + 2305);
        Assert.Equal(100, g.Ship.Fuel);
        Assert.True(g.Ship.Alive && g.Ship.Safe > 0);
        // Never back before the zone began.
        g.Idle((int)(GameEngine.StartSeconds * 60) + 2);
        g.Jump(Field.ZoneLength + 200);
        g.PutBolt(g.ShipX, g.Ship.Y);
        g.Tick(default);
        Assert.Equal("SHOT DOWN", g.Cause);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 5);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void A_ship_coming_back_starts_in_the_middle_of_the_passage()
    {
        var g = Game(2);
        g.SetLives(5);
        g.Jump(Field.ZoneLength + 4000);
        MineAt(g, 0, 0);
        g.Tick(default);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 5);
        Assert.False(GameEngine.Crashed(g.ShipX, g.Ship.Y));
        g.Idle(60);
        Assert.True(g.Ship.Alive);
    }

    [Fact]
    public void An_extra_ship_at_10000()
    {
        var g = Game();
        g.SetScore(9980);
        int lives = g.Lives;
        MineAt(g, 200, 0);
        g.Tick(Ahead);
        g.Idle(10);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_pilot_gets_through_the_first_zones()
    {
        var g = Real();
        g.SetLives(6);
        for (int i = 0; i < 60 * 240 && g.Wave < 4 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 4, $"reached zone {g.Wave}, {g.State}, lives {g.Lives}, {g.Cause}");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(2, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Scroll:0.00} {g.Ship.Y:0.00} {g.Ship.Fuel:0.00} {g.Enemies.Count}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real(2);
        g.Run(30, new Controls(Dx: 1));
        double x = g.Scroll, y = g.Ship.Y;
        g.Pause();
        g.Idle(120);
        Assert.Equal((x, y), (g.Scroll, g.Ship.Y));
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
