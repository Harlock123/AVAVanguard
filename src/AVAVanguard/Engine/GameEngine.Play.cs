namespace AVAVanguard.Engine;

public sealed partial class GameEngine
{
    public const double FlySpeedX = 320, FlySpeedY = 300, MinSx = 40, MaxSx = 640, ScrollSpeed = 110;
    public const double ShotSpeed = 1100, DropSpeed = 700, FireRepeat = 0.14, EnemyShotSpeed = 240;
    public const double StartSeconds = 2.0, DyingSeconds = 2.2, SafeSeconds = 2.5, VictorySeconds = 4.5;
    public const double FuelBurn = 2.0, FuelPerKill = 4, LowFuel = 20, SurgeSeconds = 7;
    public const int FirstExtraLife = 10000, ExtraLifeEvery = 30000, WardenPoints = 5000;

    public static int Points(EnemyKind k) => k switch
    {
        EnemyKind.Darter => 50,
        EnemyKind.Rocket => 80,
        EnemyKind.Coiler => 100,
        EnemyKind.Mine => 30,
        EnemyKind.Turret => 200,
        EnemyKind.Harrier => 150,
        _ => 60,
    };

    public static uint Tint(EnemyKind k) => k switch
    {
        EnemyKind.Darter => 0xFF6AE0FF,
        EnemyKind.Rocket => 0xFFFF6A4A,
        EnemyKind.Coiler => 0xFFFFE04A,
        EnemyKind.Mine => 0xFFFF4AD8,
        EnemyKind.Turret => 0xFF8AFF6A,
        EnemyKind.Harrier => 0xFFFF9A3A,
        _ => 0xFFB08AFF,
    };

    /// <summary>The zones passed through, counting on round after round: the first zone of the first round is 1.</summary>
    public int Wave { get; private set; } = 1;
    public int Round => (Wave - 1) / 5 + 1;
    /// <summary>The zone the ship is in.</summary>
    public Zone Zone => (Zone)((Wave - 1) % 5);
    public int Score { get; private set; }
    public int Lives { get; private set; }
    /// <summary>The place in the cave at the left edge of the screen.</summary>
    public double Scroll { get; private set; }
    public Ship Ship { get; } = new();
    public List<Shot> Shots { get; } = [];
    public List<Bolt> Bolts { get; } = [];
    public List<Enemy> Enemies { get; } = [];
    public List<Pod> Pods { get; } = [];
    public Warden Warden { get; private set; } = new();
    public List<Debris> Wreckage { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double ZoneTime { get; private set; }
    /// <summary>The bonus last paid: the fuel left at the end of a zone, or for the Warden.</summary>
    public int Bonus { get; private set; }
    /// <summary>For tests: nothing new comes, and nothing fires, by itself.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _chain;
    private double _spawnIn, _podIn, _fuelWarn;

    /// <summary>The ship's place in the cave.</summary>
    public double ShipX => Scroll + Ship.Sx;
    public double Pace => Math.Min(1.8, 1 + 0.15 * (Round - 1));
    public bool InChamber => Scroll >= Field.ChamberAt - 0.5;
    public double ScreenX(double x) => x - Scroll;
    public bool OnScreen(double x, double margin = 0) => x - Scroll >= -margin && x - Scroll <= Field.Width + margin;
    private int MaxBolts => Math.Min(12, 4 + Wave);

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = FirstExtraLife;
        Scroll = 0;
        Clear();
        Wreckage.Clear();
        ResetShip();
        SetPhase(Phase.Start);
    }

    private void Clear()
    {
        Shots.Clear();
        Bolts.Clear();
        Enemies.Clear();
        Pods.Clear();
    }

    /// <summary>Into a zone from its start: a new game, a demonstration, or the next round after the Warden.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Scroll = (wave - 1) % 5 * Field.ZoneLength;
        if ((wave - 1) % 5 == 0 || Warden.Dead) NewWarden();
        Clear();
        ResetShip();
        Ship.Fuel = 100;
        ZoneTime = 0;
        _spawnIn = 2;
        _podIn = 12;
        SetPhase(Phase.Start);
        Say(Field.NameOf(Zone), Round > 1 ? $"ROUND {Round}" : null, StartSeconds + 0.5);
    }

    private void NewWarden()
    {
        Warden = new Warden();
        for (int i = 0; i < Warden.Blocks; i++) Warden.Shield[i] = i % 8 == 0 ? 0 : Warden.BlockHits;
    }

    /// <summary>The ship at the left, in the middle of the passage, with a moment's safety.</summary>
    private void ResetShip()
    {
        var s = Ship;
        s.Sx = 160;
        var (c, f) = Field.Walls(Scroll + s.Sx);
        double lo = c, hi = f;
        for (double dx = -40; dx <= 120; dx += 20)
        {
            var (c2, f2) = Field.Walls(Scroll + s.Sx + dx);
            lo = Math.Max(lo, c2);
            hi = Math.Min(hi, f2);
        }
        s.Y = Math.Clamp((lo + hi) / 2, Field.Top + 20, Field.Height - 20);
        s.Alive = true;
        s.Surge = 0;
        s.Rams = 0;
        s.FireCool = 0;
        s.Safe = SafeSeconds;
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                FlyShip(c, dt, canFire: false);
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    Play(Sfx.ZoneStart);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                ZoneTime += dt;
                Advance(dt);
                FlyShip(c, dt, canFire: true);
                if (Phase != Phase.Play) break;
                MoveShots(dt);
                Spawn(dt);
                MoveEnemies(dt);
                MoveWarden(dt);
                if (Phase != Phase.Play) break;
                MoveBolts(dt);
                Hits();
                break;
            case Phase.Dying:
                MoveShots(dt);
                MoveEnemies(dt);
                MoveBolts(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                }
                break;
            case Phase.Victory:
                MoveShots(dt);
                if (PhaseTime > VictorySeconds) BeginWave(Wave + 1);
                break;
        }
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Vy += 120 * dt;
        }
        Wreckage.RemoveAll(d => d.Age > d.Life);
    }

    /// <summary>
    /// The cave moves on by itself, quicker when the ship pushes forward, until the Warden's chamber. Passing into
    /// a new zone pays the fuel left as a bonus.
    /// </summary>
    private void Advance(double dt)
    {
        double speed = ScrollSpeed * Pace + Math.Max(0, Ship.Sx - 420) * 0.6;
        Scroll = Math.Min(Field.ChamberAt, Scroll + speed * dt);
        if ((int)Field.ZoneAt(ShipX) > (int)Zone)
        {
            Wave++;
            Bonus = (int)Ship.Fuel * 10;
            AddScore(Bonus);
            Ship.Fuel = 100;
            ZoneTime = 0;
            Say(Field.NameOf(Zone), $"FUEL BONUS {Bonus}", 2.5);
            Play(Sfx.ZoneClear);
        }
    }

    private void Respawn()
    {
        // Back a little way, but never before the zone began.
        double zoneStart = (int)Zone * Field.ZoneLength;
        Scroll = Math.Max(zoneStart, Math.Min(Scroll, Field.ChamberAt) - 700);
        Clear();
        ResetShip();
        Ship.Fuel = 100;
        _spawnIn = 2;
        SetPhase(Phase.Start);
        Say("READY", null, StartSeconds);
    }

    // ───────────────────────────── the ship ─────────────────────────────

    private void FlyShip(Controls c, double dt, bool canFire)
    {
        var s = Ship;
        if (!s.Alive) return;
        s.Safe = Math.Max(0, s.Safe - dt);
        s.Sx = Math.Clamp(s.Sx + Math.Sign(c.Dx) * FlySpeedX * dt, MinSx, MaxSx);
        s.Y = Math.Clamp(s.Y + Math.Sign(c.Dy) * FlySpeedY * dt, Field.Top + 12, Field.Height - 12);
        if (!canFire) return;
        if (s.Surge > 0)
        {
            s.Surge -= dt;
            if (s.Surge <= 0) { s.Surge = 0; Play(Sfx.SurgeEnd); }
        }
        // Fuel burns all the time; only destroying things puts it back.
        s.Fuel = Math.Max(0, s.Fuel - FuelBurn * Pace * dt);
        if (s.Fuel < LowFuel)
        {
            _fuelWarn -= dt;
            if (_fuelWarn <= 0) { _fuelWarn = 0.8; Play(Sfx.LowFuel); }
        }
        if (s.Fuel <= 0) { Die("OUT OF FUEL"); return; }
        if (Crashed(ShipX, s.Y)) { Die("CRASHED"); return; }
        s.FireCool -= dt;
        if (c.Firing && s.FireCool <= 0 && Shots.Count < 6)
        {
            s.FireCool = FireRepeat;
            // Ahead or behind takes precedence over up or down.
            if (c.AimX != 0)
                Shots.Add(new Shot { X = ShipX + c.AimX * 22, Y = s.Y + 1, Vx = c.AimX * ShotSpeed, Life = 0.75 });
            else
                Shots.Add(new Shot { X = ShipX + 2, Y = s.Y + c.AimY * 12, Vy = Math.Sign(c.AimY) * DropSpeed, Life = 0.7 });
            Play(Sfx.Fire);
        }
        // Charge from a pod.
        foreach (var p in Pods)
        {
            if (Math.Abs(p.X - ShipX) > 30 || Math.Abs(p.Y - s.Y) > 26) continue;
            Pods.Remove(p);
            s.Surge = SurgeSeconds;
            s.Rams = 0;
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = p.X, Y = p.Y, Life = 0.6, Size = 1.5, Colour = 0xFFFFE04A });
            Say("SURGE!", null, 1.5);
            Play(Sfx.Surge);
            Play(Sfx.SurgeTune);
            break;
        }
    }

    /// <summary>Whether the ship, at a place and height, is touching the ceiling or the floor.</summary>
    public static bool Crashed(double x, double y)
    {
        for (double dx = -Ship.HalfWidth + 4; dx <= Ship.HalfWidth; dx += 8)
        {
            var (c, f) = Field.Walls(x + dx);
            if (y - Ship.HalfHeight < c || y + Ship.HalfHeight > f) return true;
        }
        return false;
    }

    private static bool InWall(double x, double y)
    {
        var (c, f) = Field.Walls(x);
        return y < c || y > f;
    }

    private void MoveShots(double dt)
    {
        foreach (var s in Shots)
        {
            double px = s.X, py = s.Y;
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            s.Life -= dt;
            if (s.Life <= 0 || !OnScreen(s.X, 20) || InWall(s.X, s.Y)) { s.Dead = true; continue; }
            // What it passed through this step, nearest first.
            foreach (var e in Enemies.OrderBy(e => Math.Abs(e.X - px) + Math.Abs(e.Y - py)))
            {
                if (e.Dead || SegmentDistance(px, py, s.X, s.Y, e.X, e.Y) > e.Radius + 4) continue;
                s.Dead = true;
                Kill(e);
                break;
            }
            if (!s.Dead && InChamber && !Warden.Dead) ShotAtWarden(s, px, py);
        }
        Shots.RemoveAll(s => s.Dead);
        Enemies.RemoveAll(e => e.Dead);
    }

    private static double SegmentDistance(double ax, double ay, double bx, double by, double px, double py)
    {
        double dx = bx - ax, dy = by - ay, len = dx * dx + dy * dy;
        double t = len > 0 ? Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len, 0, 1) : 0;
        double cx = ax + dx * t - px, cy = ay + dy * t - py;
        return Math.Sqrt(cx * cx + cy * cy);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    /// <summary>What each zone sends, a group at a time from just off the right of the screen.</summary>
    private void Spawn(double dt)
    {
        if (Calm || InChamber) return;
        _podIn -= dt;
        if (_podIn <= 0 && Scroll < Field.ChamberAt - 1600)
        {
            _podIn = 16 + _rng.NextDouble() * 8;
            double x = Scroll + Field.Width + 40;
            var (c, f) = Field.Walls(x);
            Pods.Add(new Pod { X = x, Y = (c + f) / 2 + (_rng.NextDouble() - 0.5) * (f - c) * 0.4 });
        }
        _spawnIn -= dt;
        if (_spawnIn > 0) return;
        _spawnIn = Math.Max(0.9, 2.3 / Pace - 0.05 * ((Wave - 1) % 5)) * (0.7 + 0.6 * _rng.NextDouble());
        var zone = Field.ZoneAt(Scroll + Field.Width);
        EnemyKind[] kinds = zone switch
        {
            Zone.Spire => [EnemyKind.Darter, EnemyKind.Darter, EnemyKind.Rocket, EnemyKind.Rocket],
            Zone.Band => [EnemyKind.Coiler, EnemyKind.Coiler, EnemyKind.Mine, EnemyKind.Darter],
            Zone.Barren => [EnemyKind.Turret, EnemyKind.Turret, EnemyKind.Harrier, EnemyKind.Mine, EnemyKind.Darter],
            Zone.Prism => [EnemyKind.Swarmer, EnemyKind.Swarmer, EnemyKind.Darter, EnemyKind.Rocket],
            _ => [EnemyKind.Turret, EnemyKind.Coiler, EnemyKind.Harrier, EnemyKind.Mine, EnemyKind.Darter],
        };
        SpawnGroup(kinds[_rng.Next(kinds.Length)]);
    }

    private void SpawnGroup(EnemyKind kind)
    {
        double x = Scroll + Field.Width + 40;
        var (c, f) = Field.Walls(x);
        double Mid(double margin) => c + margin + _rng.NextDouble() * Math.Max(1, f - c - 2 * margin);
        switch (kind)
        {
            case EnemyKind.Darter:
            {
                double y = Mid(70);
                for (int i = 0; i < 4; i++) Put(kind, x + i * 50, y, phase: i * 0.5);
                break;
            }
            case EnemyKind.Rocket:
                for (int i = 0; i < 1 + _rng.Next(2); i++)
                {
                    double rx = x + i * 90;
                    Put(kind, rx, Field.Floor(rx) - 14);
                }
                break;
            case EnemyKind.Coiler:
                for (int i = 0; i < 2; i++) Put(kind, x + i * 120, Mid(30), phase: _rng.Next(2) * 2 - 1);
                break;
            case EnemyKind.Mine:
                for (int i = 0; i < 3; i++) Put(kind, x + i * 110, Mid(25));
                break;
            case EnemyKind.Turret:
            {
                // On the ceiling where there is one, otherwise the floor.
                bool up = c > Field.Top + 1 && _rng.Next(2) == 0;
                var t = Put(kind, x, up ? c + 14 : f - 14);
                t.Up = up;
                break;
            }
            case EnemyKind.Harrier:
            {
                // From behind.
                double hx = Scroll - 40;
                var (hc, hf) = Field.Walls(hx);
                Put(kind, hx, (hc + hf) / 2);
                break;
            }
            case EnemyKind.Swarmer:
            {
                _chain++;
                double y = Mid(100);
                for (int i = 0; i < 6; i++)
                {
                    var e = Put(kind, x + i * 26, y);
                    e.Link = i;
                    e.Chain = _chain;
                }
                break;
            }
        }
    }

    private Enemy Put(EnemyKind kind, double x, double y, double phase = 0)
    {
        var e = new Enemy { Kind = kind, X = x, Y = y, Base = y, T = phase, FireIn = 1.2 + _rng.NextDouble() * 2.5 };
        if (kind == EnemyKind.Coiler) e.Vy = (phase < 0 ? -1 : 1) * 220;
        Enemies.Add(e);
        return e;
    }

    private void MoveEnemies(double dt)
    {
        double sx = ShipX, sy = Ship.Y;
        foreach (var e in Enemies)
        {
            if (e.Dead) continue;
            e.T += dt;
            switch (e.Kind)
            {
                case EnemyKind.Darter:
                    e.Vx = -(170 + 25 * (Round - 1));
                    e.X += e.Vx * dt;
                    e.Vy = Math.Cos(e.T * 3) * 3 * 55;
                    e.Y = e.Base + Math.Sin(e.T * 3) * 55;
                    Keep(e, 18);
                    Shoot(e, 3.2);
                    break;
                case EnemyKind.Rocket:
                    if (!e.Going && e.X - sx < 230 + (e.Base % 7) * 20 && !Calm)
                    {
                        e.Going = true;
                        if (OnScreen(e.X)) Play(Sfx.Launch);
                    }
                    if (e.Going)
                    {
                        e.Vy = Math.Max(-520, e.Vy - 700 * dt);
                        e.Y += e.Vy * dt;
                        if (e.Y < Field.Ceiling(e.X) + 6) Burst(e, quiet: true);
                    }
                    break;
                case EnemyKind.Coiler:
                {
                    e.Vx = -60 * Pace;
                    e.X += e.Vx * dt;
                    e.Y += e.Vy * Pace * dt;
                    var (c, f) = Field.Walls(e.X);
                    if (e.Y < c + 16) { e.Y = c + 16; e.Vy = Math.Abs(e.Vy); }
                    if (e.Y > f - 16) { e.Y = f - 16; e.Vy = -Math.Abs(e.Vy); }
                    break;
                }
                case EnemyKind.Mine:
                    e.Y = e.Base + Math.Sin(e.T * 2) * 12;
                    break;
                case EnemyKind.Turret:
                    Shoot(e, 2.0);
                    break;
                case EnemyKind.Harrier:
                {
                    // Its speed over the cave's, closing on the ship from wherever it is.
                    double speed = 230 * Pace, drift = ScrollSpeed * Pace;
                    double rel = e.Vx - drift;
                    rel += Math.Clamp(Math.Sign(sx - e.X) * speed - rel, -380 * dt, 380 * dt);
                    e.Vx = rel + drift;
                    e.Vy = Math.Clamp((sy - e.Y) * 2, -150, 150) + Math.Sin(e.T * 4) * 60;
                    e.X += e.Vx * dt;
                    e.Y += e.Vy * dt;
                    Keep(e, 18);
                    Shoot(e, 2.2);
                    break;
                }
                case EnemyKind.Swarmer:
                    // Every link follows the same weaving path a little behind the one before.
                    e.Vx = -150 * Pace;
                    e.X += e.Vx * dt;
                    e.Vy = Math.Cos((e.T - e.Link * 0.17) * 2.2) * 2.2 * 90;
                    e.Y = e.Base + Math.Sin((e.T - e.Link * 0.17) * 2.2) * 90;
                    Keep(e, 14);
                    if (e.Link == 0) Shoot(e, 2.8);
                    break;
            }
            if (e.X < Scroll - (e.Kind == EnemyKind.Harrier ? 400 : 150) && (e.Kind != EnemyKind.Harrier || e.T > 3)) e.Dead = true;
        }
        Enemies.RemoveAll(e => e.Dead);
    }

    /// <summary>Keeps something flying inside the passage.</summary>
    private static void Keep(Enemy e, double margin)
    {
        var (c, f) = Field.Walls(e.X);
        e.Y = Math.Clamp(e.Y, c + margin, Math.Max(c + margin, f - margin));
    }

    private bool CanFire(Enemy e) => !Calm && Phase == Phase.Play && Ship.Alive && OnScreen(e.X, -20) && Bolts.Count < MaxBolts;

    /// <summary>A shot at the ship now and then, mostly leading it a little; only from things on the screen.</summary>
    private void Shoot(Enemy e, double every)
    {
        e.FireIn -= Field.Step;
        if (e.FireIn > 0 || !CanFire(e)) return;
        e.FireIn = every / Pace * (0.7 + 0.6 * _rng.NextDouble());
        Aim(e.X, e.Y, 0);
    }

    private void Aim(double x, double y, double spread)
    {
        double speed = EnemyShotSpeed * Pace;
        double dx = ShipX - x, dy = Ship.Y - y;
        // The cave carries the ship along: lead it by the scroll.
        dx += ScrollSpeed * Pace * Math.Sqrt(dx * dx + dy * dy) / speed;
        double a = Math.Atan2(dy, dx) + spread;
        Bolts.Add(new Bolt { X = x, Y = y, Vx = Math.Cos(a) * speed, Vy = Math.Sin(a) * speed, Life = 3.5 });
        Play(Sfx.EnemyFire);
    }

    private void MoveBolts(double dt)
    {
        foreach (var b in Bolts)
        {
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;
            b.Life -= dt;
            if (b.Life <= 0 || InWall(b.X, b.Y) || !OnScreen(b.X, 40)) b.Dead = true;
        }
        Bolts.RemoveAll(b => b.Dead);
    }

    // ───────────────────────────── the Warden ─────────────────────────────

    /// <summary>The Warden's ring turns; it fires fans at the ship and sends Harriers out after it.</summary>
    private void MoveWarden(double dt)
    {
        var w = Warden;
        if (!InChamber || w.Dead) return;
        w.T += dt;
        w.Angle += 0.55 * Pace * dt;
        w.Hurt = Math.Max(0, w.Hurt - dt);
        w.Y = 440 + Math.Sin(w.T * 0.6) * 90;
        if (Calm || !Ship.Alive) return;
        w.FireIn -= dt;
        if (w.FireIn <= 0 && Bolts.Count < MaxBolts)
        {
            w.FireIn = Math.Max(0.8, 1.9 / Pace) * (0.8 + 0.4 * _rng.NextDouble());
            for (int i = -1; i <= 1; i++) Aim(w.X - Warden.CoreRadius, w.Y, i * 0.22);
        }
        w.LaunchIn -= dt;
        if (w.LaunchIn <= 0 && Enemies.Count(e => e.Kind == EnemyKind.Harrier) < 3)
        {
            w.LaunchIn = 6 / Pace;
            SpawnGroup(EnemyKind.Harrier);
        }
    }

    private void ShotAtWarden(Shot s, double px, double py)
    {
        var w = Warden;
        double d = Math.Sqrt((s.X - w.X) * (s.X - w.X) + (s.Y - w.Y) * (s.Y - w.Y));
        if (SegmentDistance(px, py, s.X, s.Y, w.X, w.Y) < Warden.CoreRadius)
        {
            s.Dead = true;
            w.Hurt = 0.15;
            AddScore(100);
            if (--w.Hp <= 0) WardenDown();
            else Play(Sfx.WardenHit);
            return;
        }
        if (Math.Abs(d - Warden.Ring) > 12) return;
        int i = Block(Math.Atan2(s.Y - w.Y, s.X - w.X));
        if (w.Shield[i] <= 0) return;
        s.Dead = true;
        w.Shield[i]--;
        AddScore(w.Shield[i] == 0 ? 50 : 10);
        Play(Sfx.ShieldHit);
    }

    /// <summary>Which shield block lies in a direction from the Warden's eye, as the ring stands now.</summary>
    public int Block(double direction)
    {
        double a = direction - Warden.Angle;
        a = (a % (Math.PI * 2) + Math.PI * 2) % (Math.PI * 2);
        return Math.Min(Warden.Blocks - 1, (int)(a / (Math.PI * 2 / Warden.Blocks)));
    }

    private void WardenDown()
    {
        var w = Warden;
        w.Dead = true;
        Bonus = WardenPoints * Round;
        AddScore(Bonus);
        for (int i = 0; i < 4; i++) Shatter(w.X + (_rng.NextDouble() - 0.5) * 160, w.Y + (_rng.NextDouble() - 0.5) * 160, 30, i % 2 == 0 ? 0xFFFF4A6A : 0xFFFFE04A);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = w.X, Y = w.Y, Life = 1.4, Size = 4, Colour = 0xFFFFE04A });
        foreach (var e in Enemies) Burst(e, quiet: true);
        Enemies.Clear();
        Bolts.Clear();
        Ship.Surge = 0;
        SetPhase(Phase.Victory);
        Music(false);
        Say("THE WARDEN FALLS", $"BONUS {Bonus}", VictorySeconds);
        Play(Sfx.WardenDown);
    }

    // ───────────────────────────── hits ─────────────────────────────

    private void Hits()
    {
        var s = Ship;
        if (!s.Alive) return;
        double x = ShipX;
        foreach (var e in Enemies)
        {
            if (e.Dead || Math.Abs(e.X - x) > Ship.HalfWidth + e.Radius * 0.75 || Math.Abs(e.Y - s.Y) > Ship.HalfHeight + e.Radius * 0.75) continue;
            if (s.Surge > 0)
            {
                // Charged, it destroys what it touches, each for twice the last.
                int points = Math.Min(3200, 100 << Math.Min(5, s.Rams));
                s.Rams++;
                Kill(e, points);
                Play(Sfx.Ram);
                continue;
            }
            if (s.Safe > 0) continue;
            Burst(e, quiet: true);
            Enemies.RemoveAll(o => o.Dead);
            Die($"HIT BY A {e.Kind.ToString().ToUpperInvariant()}");
            return;
        }
        Enemies.RemoveAll(e => e.Dead);
        foreach (var b in Bolts)
        {
            if (Math.Abs(b.X - x) > Ship.HalfWidth + 3 || Math.Abs(b.Y - s.Y) > Ship.HalfHeight + 3) continue;
            b.Dead = true;
            if (s.Surge > 0 || s.Safe > 0) continue;
            Die("SHOT DOWN");
            return;
        }
        Bolts.RemoveAll(b => b.Dead);
        if (InChamber && !Warden.Dead && Math.Sqrt((x - Warden.X) * (x - Warden.X) + (s.Y - Warden.Y) * (s.Y - Warden.Y)) < Warden.Ring + 10)
            Die("CRASHED INTO THE WARDEN");
    }

    private void Kill(Enemy e, int? points = null)
    {
        if (e.Dead) return;
        int p = points ?? (e.Kind == EnemyKind.Rocket && e.Going ? 100 : Points(e.Kind));
        AddScore(p);
        Ship.Fuel = Math.Min(100, Ship.Fuel + (e.Kind == EnemyKind.Mine ? FuelPerKill / 2 : FuelPerKill));
        if (p >= 100) Effects.Add(new Effect { Kind = EffectKind.Score, X = e.X, Y = e.Y - 20, Text = p.ToString(), Colour = p >= 400 ? 0xFFFFE04A : 0xFFFFFFFF });
        Burst(e, quiet: false);
    }

    private void Burst(Enemy e, bool quiet)
    {
        e.Dead = true;
        Shatter(e.X, e.Y, e.Kind == EnemyKind.Turret ? 18 : 12, Tint(e.Kind));
        if (!quiet) Play(e.Kind == EnemyKind.Turret ? Sfx.BigExplode : Sfx.Explode);
    }

    private void Shatter(double x, double y, int n, uint colour)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 50 + _rng.NextDouble() * 200;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = 0.5 + _rng.NextDouble() * 0.6, Colour = i % 3 == 0 ? 0xFFFFFFFF : colour });
        }
    }

    private void Die(string cause)
    {
        Cause = cause;
        var s = Ship;
        s.Alive = false;
        s.Surge = 0;
        Lives--;
        Shatter(ShipX, s.Y, 40, 0xFFFFE070);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = ShipX, Y = s.Y, Life = 1, Size = 2.2, Colour = 0xFFFFE070 });
        Bolts.Clear();
        Shots.Clear();
        SetPhase(Phase.Dying);
        Music(false);
        Play(Sfx.Death);
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a zone, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
        Ship.Safe = 0;
    }

    /// <summary>The screen moved on to a place in the cave, empty, with the ship safely in the passage.</summary>
    internal void Jump(double scroll)
    {
        Scroll = Math.Min(Field.ChamberAt, scroll);
        Clear();
        ResetShip();
        Ship.Safe = 0;
    }

    internal Enemy PutEnemy(EnemyKind kind, double x, double y)
    {
        var e = Put(kind, x, y);
        e.FireIn = 99;
        return e;
    }

    internal void PutPod(double x, double y) => Pods.Add(new Pod { X = x, Y = y });
    internal void PutBolt(double x, double y, double vx = 0, double vy = 0) => Bolts.Add(new Bolt { X = x, Y = y, Vx = vx, Vy = vy, Life = 5 });
    internal void SetShip(double sx, double y) { Ship.Sx = sx; Ship.Y = y; }
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
    internal void Begin(int wave) => BeginWave(wave);
}
