namespace TaskbarRunner.Core;

/// <summary>キー操作に合わせてキャラクターを動かし、障害物の出現、衝突、得点を処理する。</summary>
public sealed class GameSession
{
    public const double PlayerWidth = 44;
    public const double PlayerHeight = 52;
    public const double CrouchingPlayerHeight = 29;

    // Gravity は1秒で下向きに増える速さ（ピクセル/秒²）。JumpPower と SecondJumpPower は跳び始めの上向きの速さ（ピクセル/秒）。
    public const double Gravity = 1900;
    public const double JumpPower = 520;
    public const double SecondJumpPower = 360;

    public const double InitialSpeed = 320;
    public const double MaximumSpeed = 1200;
    public const double Acceleration = 4;

    public const double DifficultyRampSeconds = 200;
    // 処理が長く止まっても、再開時にゲームを進めるのは0.1秒まで。途中の衝突を見逃さないよう、1/240秒ずつ計算する。
    private const double MaximumFrameDuration = 0.1;
    private const double SimulationStepDuration = 1.0 / 240;

    private readonly Random random;
    private readonly List<Obstacle> obstacles = [];
    private double spawnRemaining;
    private double score;
    private double verticalVelocity;
    private double viewportWidth = 1280;
    private double speedMultiplier = 1;
    private bool startAtMaxSpeed;
    private bool duckHeld;
    private bool leftHeld;
    private bool rightHeld;
    // 前の障害物が消えた後も次との間隔を計算できるよう、その障害物と、出した時点の走行距離を覚えておく。
    private Obstacle? previousSpawn;
    private double previousSpawnDistance;
    private ObstacleKind nextObstacleKind;
    // 地上の障害物のすぐ後に空中の障害物を出す場合の間隔（秒）。それぞれの左端が通る時間の差で、0なら通常の出し方。
    private double followUpInterval;

    /// <summary>前回までの最高得点を受け取る。テストでは、同じ障害物の並びになるよう random を指定できる。</summary>
    public GameSession(int bestScore = 0, Random? random = null)
    {
        BestScore = Math.Max(0, bestScore);
        this.random = random ?? new Random();
    }

    public GameState State { get; private set; }
    public IReadOnlyList<Obstacle> Obstacles => obstacles;
    public double PlayerX { get; private set; } = 180;
    public double MinimumPlayerX => 32;
    public double MaximumPlayerX => Math.Min(viewportWidth * .45, 420) - PlayerWidth;
    // 左右に動く速さは設定で決まる。障害物が速くなっても、この速さは変えない。
    public double HorizontalSpeed => 220 * speedMultiplier;
    private double StartingPlayerX => Math.Clamp(viewportWidth * .15, 64, 180);
    public double HeightAboveGround { get; private set; }
    public int JumpsUsed { get; private set; }
    public bool IsGrounded => HeightAboveGround <= 0;
    public bool IsDucking { get; private set; }
    public double Elapsed { get; private set; }
    public double Distance { get; private set; }
    // 画面が狭くても避ける時間を残すため、障害物の速さに上限を設ける。キャラクターが右に寄っていても、届くまで約0.5秒を目安にする。
    // ただし、プレイ開始時の速さよりは遅くしない。その速さでも近すぎる画面幅では、0.5秒より短くなる。
    public double ScrollSpeedLimit => Math.Min(MaximumSpeed * speedMultiplier,
        Math.Max(InitialSpeed * speedMultiplier,
            (viewportWidth - MaximumPlayerX - PlayerWidth + 40) / .50));
    // startAtMaxSpeed が有効なら、時間経過を待たず最初から上限で走り、間隔も最も詰まった状態にする。
    public double Speed => startAtMaxSpeed ? ScrollSpeedLimit
        : Math.Min(ScrollSpeedLimit, (InitialSpeed + Elapsed * Acceleration) * speedMultiplier);
    public double DifficultyProgress => startAtMaxSpeed ? 1 : Math.Clamp(Elapsed / DifficultyRampSeconds, 0, 1);
    public int Score => (int)Math.Min(int.MaxValue, Math.Floor(score));
    public int BestScore { get; private set; }
    public double CurrentPlayerHeight => IsDucking ? CrouchingPlayerHeight : PlayerHeight;
    // かすっただけではぶつからないよう、衝突を調べる範囲を見た目より左右7、頭側5、足元4ピクセル小さくする。
    public Hitbox PlayerHitbox => new(PlayerX + 7,
        -HeightAboveGround - CurrentPlayerHeight + 5,
        PlayerWidth - 14, CurrentPlayerHeight - 9);
    /// <summary>ぶつかったときやプレイを中断したときに呼ぶ処理を登録する。アプリ側はここで得点やプレイ回数を保存する。</summary>
    public event Action? RunFinished;

    /// <summary>拡大する前のゲーム画面の幅と、速さの設定を受け取る。キャラクターの位置も、その幅で動ける範囲に収める。</summary>
    public void Configure(double viewportWidth, double multiplier, bool startAtMaxSpeed = false)
    {
        if (!double.IsFinite(viewportWidth) || viewportWidth < 320)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (!double.IsFinite(multiplier) || multiplier < .75 || multiplier > 1.25)
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        this.viewportWidth = viewportWidth;
        speedMultiplier = multiplier;
        this.startAtMaxSpeed = startAtMaxSpeed;
        PlayerX = State is GameState.Idle or GameState.Ready
            ? StartingPlayerX : Math.Clamp(PlayerX, MinimumPlayerX, MaximumPlayerX);
    }

    /// <summary>プレイ中なら終了し、新しく始められるようにして SPACE キーを待つ。</summary>
    public void Ready()
    {
        Stop();
        Reset();
        State = GameState.Ready;
    }

    /// <summary>開始前かゲームオーバーなら、得点や位置を戻してプレイを始める。すでにプレイ中なら何もしない。</summary>
    public void Start()
    {
        if (State is not (GameState.Ready or GameState.GameOver)) return;
        Reset();
        State = GameState.Playing;
    }

    /// <summary>ジャンプできたら true を返す。着地するまでに2回跳べる。しゃがんでいる間は跳べない。</summary>
    public bool Jump()
    {
        if (State != GameState.Playing || JumpsUsed >= 2 || IsDucking) return false;
        JumpsUsed++;
        // 上向きの速さを設定し直すので、落下中でも2段目のジャンプで上に跳べる。
        verticalVelocity = JumpsUsed == 1 ? JumpPower : SecondJumpPower;
        return true;
    }

    /// <summary>下キーを押している間はしゃがむ。空中で押した場合は、押したまま着地するとしゃがむ。</summary>
    public void Duck(bool pressed)
    {
        duckHeld = State == GameState.Playing && pressed;
        IsDucking = duckHeld && IsGrounded;
    }

    public void MoveLeft(bool pressed) => leftHeld = State == GameState.Playing && pressed;
    public void MoveRight(bool pressed) => rightHeld = State == GameState.Playing && pressed;

    /// <summary>プレイを止め、キーを押していない状態に戻す。すでに終了していれば、終了の処理は繰り返さない。</summary>
    public void Stop()
    {
        var finish = State == GameState.Playing;
        State = GameState.Idle;
        ClearInput();
        verticalVelocity = 0;
        if (finish) FinishRun();
    }

    /// <summary>前回から何秒たったかを受け取り、最大0.1秒ぶんゲームを進める。少しずつ動かして衝突を調べ、速い障害物も見逃さない。</summary>
    public void Update(double deltaTime)
    {
        if (State != GameState.Playing || !double.IsFinite(deltaTime) || deltaTime <= 0) return;
        var remaining = Math.Min(deltaTime, MaximumFrameDuration);
        while (remaining > 0 && State == GameState.Playing)
        {
            var dt = Math.Min(remaining, SimulationStepDuration);
            Step(dt);
            remaining -= dt;
        }
    }

    private void Step(double dt)
    {
        Elapsed += dt;
        Distance += Speed * dt;
        score += dt * 10; // 速さに関係なく、1秒遊ぶごとに10点増える。

        UpdatePlayer(dt);
        UpdateObstacleSpawning(dt);
        MoveObstacles(dt);
        CheckCollisions();
    }

    private void UpdatePlayer(double dt)
    {
        var direction = (rightHeld ? 1 : 0) - (leftHeld ? 1 : 0);
        PlayerX = Math.Clamp(PlayerX + direction * HorizontalSpeed * dt, MinimumPlayerX, MaximumPlayerX);
        if (verticalVelocity != 0 || !IsGrounded)
        {
            HeightAboveGround += verticalVelocity * dt - .5 * Gravity * dt * dt;
            verticalVelocity -= Gravity * dt;
            if (HeightAboveGround <= 0)
            {
                HeightAboveGround = 0;
                verticalVelocity = 0;
                JumpsUsed = 0;
            }
        }

        IsDucking = duckHeld && IsGrounded;
    }

    private void UpdateObstacleSpawning(double dt)
    {
        spawnRemaining -= dt;
        if (spawnRemaining <= 0 && HasRecoverySpace())
        {
            var kind = nextObstacleKind;
            var obstacleWidth = kind switch
            {
                ObstacleKind.GroundBar => random.Next(16, 37),
                ObstacleKind.TallBar => random.Next(24, 37),
                ObstacleKind.WideBar => WideObstacleWidth(),
                _ => random.Next(56, 113)
            };
            previousSpawn = new Obstacle(viewportWidth + 40, kind, obstacleWidth);
            previousSpawnDistance = Distance;
            obstacles.Add(previousSpawn);
            ScheduleNextObstacle();
        }
    }

    private void ScheduleNextObstacle()
    {
        // 空中の障害物は少なめにし、続けて出さない。高い障害物と幅広の障害物も連続しないようにする。
        nextObstacleKind = previousSpawn?.Kind != ObstacleKind.OverheadBar && random.NextDouble() < .25
            ? ObstacleKind.OverheadBar : ObstacleKind.GroundBar;
        if (nextObstacleKind == ObstacleKind.GroundBar && Elapsed >= 8 &&
            previousSpawn?.Kind is not (ObstacleKind.TallBar or ObstacleKind.WideBar))
        {
            var choice = random.NextDouble();
            if (choice < .25) nextObstacleKind = ObstacleKind.TallBar;
            else if (Elapsed >= 12 && choice < .43) nextObstacleKind = ObstacleKind.WideBar;
        }

        // ときどき、地上の障害物のすぐ後に空中の障害物を出す。1回跳んですぐ着地すると避けやすい組み合わせ。
        followUpInterval = previousSpawn?.Kind == ObstacleKind.GroundBar &&
            nextObstacleKind == ObstacleKind.OverheadBar && random.NextDouble() < .60
            ? .43 + random.NextDouble() * .04 : 0;
        // すぐ後に続けて出す場合は、HasRecoverySpace で間隔を確認するだけにして、追加の待ち時間は設けない。
        spawnRemaining = followUpInterval > 0 ? 0 : NextSpawnInterval();
    }

    private void MoveObstacles(double dt)
    {
        foreach (var obstacle in obstacles)
            obstacle.X -= Speed * dt;
        obstacles.RemoveAll(obstacle => obstacle.X + obstacle.Width < 0);
    }

    private double WideObstacleWidth()
    {
        // 速くなっても簡単に飛び越せないよう、幅広の障害物を長くする。キャラクターに届くころの速さを見積もる。
        var travelSeconds = (viewportWidth + 40 - MinimumPlayerX) / Speed;
        var arrivalSpeed = Math.Min(ScrollSpeedLimit,
            Speed + Acceleration * speedMultiplier * (travelSeconds + 1));
        // 衝突する可能性のある時間が約0.62秒続く幅にする。飛び越すには2段ジャンプで長く空中にいる必要がある。
        // 衝突を調べる範囲は見た目より小さいので、その差を幅に含める。小数を切り上げ、保存して使い回す模様の種類も減らす。
        return Math.Ceiling(arrivalSpeed * .62 - (PlayerHitbox.Width - 8));
    }

    private void CheckCollisions()
    {
        if (obstacles.Any(obstacle => PlayerHitbox.Intersects(obstacle.Hitbox)))
        {
            State = GameState.GameOver;
            ClearInput();
            FinishRun();
        }
    }

    private double NextSpawnInterval()
    {
        var choice = random.NextDouble();
        var gap = choice < .35
            ? .45 + random.NextDouble() * .20
            : .65 + random.NextDouble() * .40;
        return gap * (1 - DifficultyProgress * .25);
    }

    private bool HasRecoverySpace()
    {
        if (previousSpawn is null) return true;
        // キャラクターが一番左にいる場合を基準にする。障害物が届くまでに最も長くかかる位置。
        var travelDistance = viewportWidth + 40 - MinimumPlayerX;
        // 障害物は近づく間にも速くなる。今の速さで距離を割ると、実際より長めの時間が出る。
        // その時間ぶん速くなると見積もり、障害物の間隔を広めに取る。広い画面でも、届いたときに避ける時間を残す。
        var arrivalSpeed = Math.Min(ScrollSpeedLimit,
            Speed + Acceleration * speedMultiplier * travelDistance / Speed);
        if (followUpInterval > 0)
        {
            // 2つの障害物の左端が通る時間の差をそろえる。速くなっても、ジャンプ後に着地する時間を同じくらい残す。
            return Distance - previousSpawnDistance >= arrivalSpeed * followUpInterval;
        }
        // 高い障害物や幅広の障害物を越えた後は、着地して次に跳ぶまで長めに待つ。地上の障害物の次が高い場合も長めにする。
        var recoverySeconds = previousSpawn.Kind switch
        {
            ObstacleKind.TallBar or ObstacleKind.WideBar => .72,
            ObstacleKind.GroundBar when nextObstacleKind == ObstacleKind.TallBar => .70,
            ObstacleKind.GroundBar => .52,
            _ => .42
        };
        // キャラクターの体が全部通り過ぎてから次に跳ぶ時間を取れるよう、障害物の間隔に体の幅も足す。
        var minimumGap = arrivalSpeed * recoverySeconds + PlayerWidth;
        return Distance - previousSpawnDistance - previousSpawn.Width >= minimumGap;
    }

    // 別のアプリに切り替わると、キーを離したことが伝わらない場合がある。次に遊ぶときまで押しっぱなし扱いにならないよう、すべて解除する。
    private void ClearInput()
    {
        IsDucking = false;
        duckHeld = false;
        leftHeld = false;
        rightHeld = false;
    }

    // 終了時に呼ばれる保存処理が新しい最高得点を読めるよう、先に得点を更新する。
    private void FinishRun()
    {
        BestScore = Math.Max(BestScore, Score);
        RunFinished?.Invoke();
    }

    // 次のプレイのために得点や位置を元に戻す。最高得点、画面幅、速さの設定はそのまま残す。
    private void Reset()
    {
        obstacles.Clear();
        score = 0;
        Elapsed = 0;
        Distance = 0;
        HeightAboveGround = 0;
        JumpsUsed = 0;
        verticalVelocity = 0;
        ClearInput();
        PlayerX = StartingPlayerX;
        previousSpawn = null;
        previousSpawnDistance = 0;
        nextObstacleKind = random.NextDouble() < .25 ? ObstacleKind.OverheadBar : ObstacleKind.GroundBar;
        followUpInterval = 0;
        spawnRemaining = .9;
    }
}
