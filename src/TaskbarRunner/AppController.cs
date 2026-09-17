using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TaskbarRunner.Core;
using TaskbarRunner.Platform;
using TaskbarRunner.Rendering;
using TaskbarRunner.Tray;

namespace TaskbarRunner;

/// <summary>
/// アイコンのクリックやキー操作を受けて、ゲームを進めたり、設定や記録を保存したりする。
/// ゲームの動きは <see cref="GameSession"/> で計算し、画面の表示には <see cref="OverlayWindow"/> を使う。
/// </summary>
internal sealed class AppController : IDisposable
{
	// 設定とプレイ記録は別々のファイルに保存する。片方が壊れても、もう片方は読める。
	private readonly JsonStore<GameSettings> settingsStore;
	private readonly JsonStore<SaveData> scoreStore;
	private readonly GameSession game;
	private readonly TrayController tray;
	private readonly DispatcherTimer recoveryTimer;
	// ゲームを進める時間は描画回数から推定せず、実際の経過時間を測る。
	private readonly Stopwatch clock = new();
	private OverlayWindow overlay;
	// 開いている設定画面を覚えておき、2つ目を開かないようにする。閉じているときは null。
	private SettingsWindow? settingsWindow;
	private GameSettings settings;
	private SaveData save;
	private double lastFrame;
	private TimeSpan? lastRendering;
	private double frameBudget;
	private int recoveryAttempts;
	// 遊ぶ直前に使っていたウィンドウ。ゲーム画面を隠したら、このウィンドウを再び操作できるようにする。
	private nint previousWindow;
	private bool disposed;
	internal AppCommands Commands { get; }

	internal AppController(string? dataFolder = null, bool notifyStartup = true)
	{
		// 通常のユーザーでも保存できるよう、自分の AppData\Roaming\TaskbarRunner フォルダーを使う。
		// 結合テストでは一時フォルダーを指定し、利用者の設定と記録を変更しない。
		var folder = dataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarRunner");
		settingsStore = new JsonStore<GameSettings>(Path.Combine(folder, "settings.json"));
		scoreStore = new JsonStore<SaveData>(Path.Combine(folder, "save.json"));
		// ファイルを直接書き換えて使えない値が入っていても動くよう、読み込んだ値を Sanitize で直す。
		settings = settingsStore.Load().Sanitize();
		save = scoreStore.Load().Sanitize();
		game = new GameSession(save.BestScore);
		game.RunFinished += SaveRun;
		recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
		recoveryTimer.Tick += HandleRecoveryTick;
		overlay = CreateOverlay();
		Commands = new AppCommands(() => Queue(Play), () => Queue(OpenSettings),
				() => Queue(RestartOverlay), () => Queue(() => Application.Current.Shutdown()));
		tray = new TrayController(Commands);
		TryPosition(out _);
		if (notifyStartup) NotifyStartup();
	}

	private void NotifyStartup()
	{
		var errors = new List<string>();
		if (settingsStore.LastError is { } settingsError) errors.Add(settingsError);
		if (scoreStore.LastError is { } scoreError) errors.Add(scoreError);
		tray.Notify(errors.Count > 0 ? string.Join("\n", errors) : "通知領域のロボットを左クリックして遊べます。右クリックは設定・終了メニュー。SPACE でスタート／ジャンプ、ESC で一時停止、もう一度 ESC で作業に戻ります。", errors.Count > 0);
	}

	// アイコンからの操作は、画面を扱う処理の列（UI スレッド）に渡す。別の処理の列から直接画面を変えないため。
	private static void Queue(Action action) => Application.Current.Dispatcher.BeginInvoke(action);

	private OverlayWindow CreateOverlay()
	{
		var window = new OverlayWindow(game);
		window.FocusLost += () => Suspend(restoreFocus: false);
		window.EnvironmentChanged += Recover;
		window.KeyPressed += HandleKey;
		window.KeyReleased += HandleKeyReleased;
		return window;
	}

	private void Play()
	{
		if (disposed) return;
		if (settingsWindow is not null)
		{
			settingsWindow.Activate();
			return;
		}
		Suspend(restoreFocus: false);
		if (!TryPosition(out var error))
		{
			tray.Notify(error, true);
			return;
		}
		previousWindow = NativeMethods.GetForegroundWindow();
		// 一時停止した走行が残っていれば、やり直さずその続きを出す。
		if (game.State != GameState.Paused) game.Ready();
		overlay.Redraw();
		if (!overlay.BeginInteraction())
		{
			Suspend(restoreFocus: false);
			tray.Notify("ゲームへフォーカスを移せませんでした。通知領域のロボットをもう一度左クリックしてください。", true);
		}
	}

	private void HandleKey(Key key)
	{
		switch (key)
		{
			// ESC は1回目でその場で止め、2回目で画面を隠して作業に戻す。
			case Key.Escape when game.State == GameState.Playing:
				PauseInPlace();
				break;
			case Key.Escape:
				Suspend(restoreFocus: true);
				break;
			case Key.Space when game.State == GameState.Paused:
				ResumeRun();
				break;
			case Key.R when game.State == GameState.Paused:
				game.Ready();
				overlay.Redraw();
				break;
			case Key.Space when game.State is GameState.Ready or GameState.GameOver:
				StartRun();
				break;
			case Key.Space:
			case Key.Up:
				game.Jump();
				break;
			case Key.Down:
				game.Duck(true);
				break;
			case Key.Left:
				game.MoveLeft(true);
				break;
			case Key.Right:
				game.MoveRight(true);
				break;
		}
	}

	private void HandleKeyReleased(Key key)
	{
		switch (key)
		{
			case Key.Down:
				game.Duck(false);
				break;
			case Key.Left:
				game.MoveLeft(false);
				break;
			case Key.Right:
				game.MoveRight(false);
				break;
		}
	}

	private void StartRun()
	{
		game.Start();
		BeginFrames();
	}

	private void ResumeRun()
	{
		game.Resume();
		BeginFrames();
	}

	private void BeginFrames()
	{
		StopRendering();
		lastFrame = 0;
		lastRendering = null;
		frameBudget = 0;
		clock.Restart();
		CompositionTarget.Rendering += Tick;
		overlay.Redraw();
	}

	private void Tick(object? sender, EventArgs e)
	{
		if (!clock.IsRunning) return;
		// 他のアプリへの切り替えを見逃してもゲームが進み続けないよう、ここでも操作中のウィンドウを確認する。
		if (NativeMethods.GetForegroundWindow() != overlay.Handle)
		{
			Suspend(restoreFocus: false);
			return;
		}
		var renderingTime = ((RenderingEventArgs)e).RenderingTime;
		if (lastRendering == renderingTime) return; // 同じ描画時刻の通知で二重更新しない。
		var interval = 1.0 / settings.FpsLimit;
		frameBudget += lastRendering is { } previous ? (renderingTime - previous).TotalSeconds : interval;
		lastRendering = renderingTime;
		if (frameBudget + 1e-7 < interval) return;
		// 端数を持ち越す。遅れた場合も1回の描画で過去のフレームをまとめて描き直さない。
		frameBudget = Math.Max(0, (frameBudget - interval) % interval);
		var now = clock.Elapsed.TotalSeconds;
		// 前回から実際にたった時間ぶん進める。画面を描く回数（FPS）の設定を変えても、ゲームの速さが変わらないようにする。
		game.Update(now - lastFrame);
		lastFrame = now;
		if (game.State == GameState.GameOver)
		{
			StopRendering();
		}
		overlay.Redraw();
	}

	private void StopRendering()
	{
		CompositionTarget.Rendering -= Tick;
		clock.Stop();
	}

	/// <summary>ゲーム画面を出したまま走行を止める。もう一度 ESC を押すと隠れる。</summary>
	private void PauseInPlace()
	{
		StopRendering();
		game.Pause();
		overlay.Redraw();
	}

	/// <summary>走行を残したままゲーム画面を隠す。作業に戻った後、通知領域から続きを開ける。</summary>
	private void Suspend(bool restoreFocus) => Hide(restoreFocus, pause: true);

	/// <summary>プレイを終えてゲーム画面を隠す。記録はここで確定する。</summary>
	private void Idle(bool restoreFocus) => Hide(restoreFocus, pause: false);

	/// <summary>restoreFocus が true なら、遊ぶ前のウィンドウに操作を戻す。</summary>
	private void Hide(bool restoreFocus, bool pause)
	{
		// ゲームを操作中だったか、隠す前に覚えておく。EndInteraction で隠した後では分からない。
		var hadFocus = NativeMethods.GetForegroundWindow() == overlay.Handle;
		StopRendering();
		if (pause) game.Pause();
		else game.Stop();
		overlay.EndInteraction();
		if (restoreFocus && hadFocus && previousWindow != overlay.Handle && NativeMethods.IsWindow(previousWindow))
			NativeMethods.SetForegroundWindow(previousWindow);
	}

	private bool TryPosition(out string error)
	{
		if (!TaskbarLocator.TryLocate(out var placement, out error)) return false;
		overlay.Position(placement, settings);
		// ゲーム内の計算には拡大前の幅を使う。画面の実際の幅を、Windows の表示倍率とキャラクターの倍率で割る。
		game.Configure(Math.Max(320, placement.Width / placement.DpiScale / settings.CharacterScale), settings.GameSpeed, settings.StartAtMaxSpeed);
		return true;
	}

	// 画面設定の変更やタスクバーの再起動後は、ゲームの位置を調べ直す。位置が落ち着くまで少し待ってから始める。
	private void Recover()
	{
		if (disposed) return;
		Suspend(restoreFocus: false);
		recoveryAttempts = 0;
		recoveryTimer.Stop();
		recoveryTimer.Start(); // 変更があったときだけ調べ直す。成功するか、決めた回数に達したら止める。
	}

	// 0.5秒おきに位置を調べる。分かった時点で止め、分からなくても6回（約3秒）でやめる。
	private void HandleRecoveryTick(object? sender, EventArgs e)
	{
		if (TryPosition(out _) || ++recoveryAttempts >= 6)
			recoveryTimer.Stop();
	}

	// メニューの Restart Overlay を押したときの処理。表示の乱れを直すため、ゲームのウィンドウを作り直す。
	private void RestartOverlay()
	{
		Suspend(restoreFocus: true);
		recoveryTimer.Stop();
		overlay.Close();
		overlay = CreateOverlay();
		if (!TryPosition(out var error)) tray.Notify(error, true);
	}

	private void OpenSettings()
	{
		Suspend(restoreFocus: false);
		if (settingsWindow is not null)
		{
			settingsWindow.Activate();
			return;
		}
		settingsWindow = new SettingsWindow(settings, save, SaveSettings);
		try
		{
			settingsWindow.ShowDialog();
		}
		finally
		{
			// エラーで閉じた場合も「設定画面は閉じている」状態に戻す。残すと、次に Play を押してもゲームが開かない。
			settingsWindow = null;
		}
	}

	private bool SaveSettings(GameSettings updated)
	{
		var sanitized = updated.Sanitize();
		if (!settingsStore.Save(sanitized))
		{
			MessageBox.Show(settingsWindow, settingsStore.LastError!, "設定を保存できません", MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}

		settings = sanitized;
		TryPosition(out _);
		return true;
	}

	private void SaveRun()
	{
		save = save with
		{
			BestScore = Math.Max(save.BestScore, game.BestScore),
			TotalRuns = save.TotalRuns < long.MaxValue ? save.TotalRuns + 1 : long.MaxValue,
			TotalDistance = save.TotalDistance + game.Distance
		};
		if (!scoreStore.Save(save)) tray.Notify(scoreStore.LastError!, true);
	}

	public void Dispose()
	{
		if (disposed) return;
		disposed = true;
		recoveryTimer.Stop();
		Idle(restoreFocus: true);
		game.RunFinished -= SaveRun;
		settingsWindow?.Close();
		overlay.Close();
		tray.Dispose();
	}
}
