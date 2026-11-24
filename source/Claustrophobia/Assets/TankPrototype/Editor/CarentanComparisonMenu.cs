using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

internal static class CarentanComparisonMenu
{
	private enum Phase { Idle, Entering, Playing, Exiting, Cancelling }

	private const string RunMenu = "Tools/Carentan/Run A Cover Comparison";
	private const string StopMenu = "Tools/Carentan/Stop A Cover Comparison";
	private const string Prefix = "Carentan.Comparison.";
	private const string PhaseKey = Prefix + "Phase";
	private const string ReportKey = Prefix + "Report";
	private const string OptionsKey = Prefix + "PlayOptions";
	private const double CaseTimeout = 95d;

	private static CarentanDrive drive;
	private static double playBegan;

	private static Phase State
	{
		get => (Phase)SessionState.GetInt(PhaseKey, 0);
		set => SessionState.SetInt(PhaseKey, (int)value);
	}

	[InitializeOnLoadMethod]
	private static void Resume()
	{
		if (State != Phase.Idle) EditorApplication.update += Tick;
	}

	[MenuItem(RunMenu, true)]
	private static bool CanRun() => State == Phase.Idle && !EditorApplication.isPlayingOrWillChangePlaymode;

	[MenuItem(RunMenu)]
	private static void Run()
	{
		if (!CanRun()) return;
		Scene scene = SceneManager.GetActiveScene();
		if (scene.path != CarentanComparison.ScenePath || scene.isDirty || SceneManager.sceneCount != 1)
		{
			Debug.LogError("[Carentan] Open the saved Carentan scene by itself before starting the comparison.");
			return;
		}

		string folder = Path.Combine(Application.dataPath, "../Builds/CarentanComparisons",
			DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
		Directory.CreateDirectory(folder);
		string report = Path.GetFullPath(Path.Combine(folder, "comparison.tsv"));
		File.WriteAllText(report, CarentanComparison.Header + Environment.NewLine);
		SessionState.SetString(ReportKey, report);
		SessionState.SetInt(OptionsKey, (int)EditorSettings.enterPlayModeOptions);
		CarentanComparison.RunIndex = 0;
		EditorApplication.update += Tick;
		State = Phase.Exiting;
		Debug.Log($"[Carentan] Starting {CarentanComparison.RunCount} comparison runs. {report}");
	}

	[MenuItem(StopMenu, true)]
	private static bool CanStop() => State != Phase.Idle;

	[MenuItem(StopMenu)]
	private static void Stop()
	{
		if (!CanStop()) return;
		State = Phase.Cancelling;
		if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
	}

	private static void Tick()
	{
		if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

		if (State == Phase.Cancelling)
		{
			if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(false);
			return;
		}

		if (State == Phase.Exiting)
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode) return;
			if (CarentanComparison.RunIndex >= CarentanComparison.RunCount)
			{
				Finish(true);
				return;
			}

			// Reload the scene and domain for a fresh Fusion runner, patrol, chamber and projectiles.
			EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
			State = Phase.Entering;
			EditorApplication.EnterPlaymode();
			return;
		}

		if (State == Phase.Entering)
		{
			if (!EditorApplication.isPlaying)
			{
				if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(false);
				return;
			}

			drive = Object.FindFirstObjectByType<CarentanDrive>();
			playBegan = EditorApplication.timeSinceStartup;
			State = Phase.Playing;
		}

		if (State != Phase.Playing) return;
		if (!EditorApplication.isPlaying)
		{
			if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(false);
			return;
		}

		try
		{
			Collect();
		}
		catch (Exception error)
		{
			Debug.LogException(error);
			Stop();
		}
	}

	private static void Collect()
	{
		int run = CarentanComparison.RunIndex;
		if (drive == null) drive = Object.FindFirstObjectByType<CarentanDrive>();
		var comparison = drive != null ? drive.Comparison : null;
		bool timedOut = EditorApplication.timeSinceStartup - playBegan > CaseTimeout;
		if (comparison?.Finished == true && (!string.IsNullOrEmpty(drive.DeploymentLogPath) || !drive.DeploymentStopped))
			WriteRow(comparison.GetRow(run));
		else if (timedOut)
			WriteRow($"{run + 1}\t{CarentanComparison.Strategy(run)}\ttimeout: {comparison?.Status ?? "scene startup"}" + new string('\t', 14));
		else return;

		CarentanComparison.RunIndex = run + 1;
		State = Phase.Exiting;
		EditorApplication.ExitPlaymode();
	}

	private static void WriteRow(string row)
	{
		File.AppendAllText(SessionState.GetString(ReportKey, ""), row + Environment.NewLine);
		Debug.Log("[Carentan comparison] " + row);
	}

	private static void Finish(bool completed)
	{
		EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(OptionsKey, 0);
		string path = SessionState.GetString(ReportKey, "");
		SessionState.EraseInt(OptionsKey);
		SessionState.EraseString(ReportKey);
		SessionState.EraseInt(PhaseKey);
		CarentanComparison.RunIndex = -1;
		EditorApplication.update -= Tick;
		drive = null;
		Debug.Log($"[Carentan] Comparison {(completed ? "finished" : "stopped")}. {path}");
	}
}
