using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class DriverStatusVerify
{
	private const string Folder = "Builds/DriverStatus";
	private const string Key = "DriverStatusVerification";
	private static IEnumerator run;
	private static double deadline;
	private static readonly List<string> log = new List<string>();
	[Serializable] private class Setup { public Entry[] Scenes; }
	[Serializable] private class Entry { public string Path; public bool Loaded; public bool Active; }

	static DriverStatusVerify()
	{
		EditorApplication.playModeStateChanged += OnPlayMode;
		if (SessionState.GetBool(Key, false) && EditorApplication.isPlaying) EditorApplication.delayCall += StartRun;
		else if (File.Exists(Path.Combine(Application.dataPath, "../" + Folder + "/verify.request")))
			EditorApplication.delayCall += Prepare;
	}

	[MenuItem("Tools/Driver Status/Verify Authored Panel")]
	public static void Prepare()
	{
		Directory.SetCurrentDirectory(Path.GetDirectoryName(Application.dataPath));
		if (EditorApplication.isPlayingOrWillChangePlaymode) return;
		try
		{
			if (!File.Exists(Folder + "/prepare-result.txt")) throw new InvalidOperationException("Authoring has not passed.");
			if (File.Exists(Folder + "/verify.request")) File.Delete(Folder + "/verify.request");
			SceneSetup[] current = EditorSceneManager.GetSceneManagerSetup();
			var saved = new Setup { Scenes = new Entry[current.Length] };
			for (int i = 0; i < current.Length; i++)
			{
				if (string.IsNullOrEmpty(current[i].path) || SceneManager.GetSceneByPath(current[i].path).isDirty)
					throw new InvalidOperationException("Unsaved scenes; verification stopped.");
				saved.Scenes[i] = new Entry { Path = current[i].path, Loaded = current[i].isLoaded, Active = current[i].isActive };
			}
			SessionState.SetString(Key + "Scenes", JsonUtility.ToJson(saved));
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			EditorSceneManager.OpenScene("Assets/TankPrototype/Scenes/TankInterior.unity", OpenSceneMode.Additive);
			SessionState.SetBool(Key, true);
			EditorApplication.isPlaying = true;
		}
		catch (Exception e) { File.WriteAllText(Folder + "/verify-error.txt", e.ToString()); Debug.LogException(e); }
	}

	private static void OnPlayMode(PlayModeStateChange state)
	{
		if (!SessionState.GetBool(Key, false)) return;
		if (state == PlayModeStateChange.EnteredPlayMode) StartRun();
		if (state != PlayModeStateChange.EnteredEditMode) return;

		EditorApplication.update -= Tick;
		SessionState.SetBool(Key, false);
		Setup saved = JsonUtility.FromJson<Setup>(SessionState.GetString(Key + "Scenes", ""));
		var restore = new SceneSetup[saved.Scenes.Length];
		for (int i = 0; i < restore.Length; i++) restore[i] = new SceneSetup
		{
			path = saved.Scenes[i].Path, isLoaded = saved.Scenes[i].Loaded, isActive = saved.Scenes[i].Active
		};
		EditorSceneManager.RestoreSceneManagerSetup(restore);
		File.WriteAllText(Folder + "/verify-restored.txt", "Restored original scenes after Play Mode.");
	}

	private static void StartRun()
	{
		if (run != null) return;
		deadline = EditorApplication.timeSinceStartup + 60;
		run = Verify();
		EditorApplication.update -= Tick;
		EditorApplication.update += Tick;
	}

	private static void Tick()
	{
		if (!EditorApplication.isPlaying || run == null) return;
		try
		{
			if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Verification timed out.");
			if (run.MoveNext()) return;
			log.Add("PASS: Native Unity Play Mode verification finished (offline UI fixture).");
			File.WriteAllLines(Folder + "/verify-result.txt", log);
			Finish();
		}
		catch (Exception e)
		{
			File.WriteAllText(Folder + "/verify-error.txt", string.Join("\n", log) + "\n" + e);
			Debug.LogException(e);
			Finish();
		}
	}

	private static void Finish()
	{
		run = null;
		EditorApplication.update -= Tick;
		EditorApplication.isPlaying = false;
	}

	private static IEnumerator Verify()
	{
		yield return null;
		yield return null;
		TankInterior interior = Object.FindAnyObjectByType<TankInterior>();
		Require(interior != null && interior.Prepare(), "Authored interior references and seat preparation");
		Require(interior.Seats.ApplyRole(TankCrew.Role.Driver), "Apply actual driver seat");
		CrewInteract user = interior.Interaction;
		foreach (CrewLook look in user.GetComponents<CrewLook>()) look.enabled = false;
		foreach (CrewDebugView debug in user.GetComponents<CrewDebugView>()) debug.enabled = false;
		interior.Mode.enabled = false;
		user.View.gameObject.SetActive(true);
		user.View.enabled = true;
		user.enabled = true;
		foreach (Renderer avatar in user.GetComponentsInChildren<Renderer>(true))
			avatar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
		var fixture = new GameObject("DriverStatusOfflineFixture");
		Rigidbody body = fixture.AddComponent<Rigidbody>();
		body.useGravity = false;
		TankMotor motor = fixture.AddComponent<TankMotor>();
		TankControl control = fixture.AddComponent<TankControl>();
		TankHealth health = fixture.AddComponent<TankHealth>();
		TankStatus status = fixture.AddComponent<TankStatus>();
		Set(status, "health", health); Set(status, "motor", motor);
		Require(control.Bind(interior.Crew, interior.DriveDevice, interior.AimDevice, interior.Sight), "Bind actual crew controls");
		foreach (TankLever lever in interior.Levers) lever.Bind(control);
		TankStatusPanel panel = interior.StatusPanel;
		Require(panel.Bind(status, control), "Bind authored status panel");
		foreach (CrewDevice device in user.Devices) Require(device.name != "DriverMap", "Read-only map excluded from device selection");
		Image fill = Get<Image>(panel, "hpFill");
		TMP_Text text = Get<TMP_Text>(panel, "hpText");
		RectTransform left = Get<RectTransform>(panel, "leftHandle");
		RectTransform right = Get<RectTransform>(panel, "rightHandle");
		CanvasGroup flash = Get<CanvasGroup>(panel, "hitBorder");
		CanvasGroup content = Get<CanvasGroup>(panel, "content");
		Require(panel.GetComponentsInChildren<TMP_Text>(true).Length == 1, "Only percentage text; no labels or gear stages");
		foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true)) Require(!graphic.raycastTarget, "Display-only graphics");
		Require(panel.gameObject.layer == 9, "Interior-only layer");
		Require(Mathf.Approximately(panel.transform.lossyScale.x, panel.transform.lossyScale.y), "Uniform world-space scaling");
		Transform map = GameObject.Find("DriverMap").transform;
		Require(Vector3.Distance(panel.transform.position, map.position) < 1f, "Panel stays on DriverMap after scene reload");
		user.View.transform.LookAt(interior.DriveDevice.Parts[0].bounds.center);
		Physics.SyncTransforms();
		user.RefreshSelection();
		Require(user.Selected == interior.DriveDevice && user.TryUse(interior.DriveDevice) && control.IsDriving, "Actual drive-device selection and begin drive");
		yield return null;
		Require(text.text == "100%" && fill.fillAmount == 1f && content.alpha == 1f && flash.alpha == 0f, "Initial HP and no false hit flash");

		float[] inputs = { -1f, -0.5f, 0f, 0.5f, 1f };
		for (int i = 0; i < inputs.Length; i++)
		{
			control.SetLeverInputs(user, inputs[i], -inputs[i]);
			yield return null;
			float expectedLeft = -204f + 246f * (i * 0.25f);
			float expectedRight = 42f - 246f * (i * 0.25f);
			Require(Mathf.Abs(left.anchoredPosition.y - expectedLeft) < 0.01f
				&& Mathf.Abs(right.anchoredPosition.y - expectedRight) < 0.01f, "Independent continuous lever position " + inputs[i]);
		}
		control.SetLeverInputs(user, 0.65f, -0.3f);
		yield return null;
		Require(Mathf.Abs(left.anchoredPosition.y - right.anchoredPosition.y) > 100f, "Non-step lever values remain distinct");
		user.View.transform.LookAt(panel.transform.position);
		Canvas.ForceUpdateCanvases();
		Require(Capture(user.View, Folder + "/driver-view.png", 1500, 1000) > 50, "HP bar visible from actual driver camera");
		var closeup = new GameObject("StatusCloseupCamera", typeof(Camera));
		Camera camera = closeup.GetComponent<Camera>();
		camera.orthographic = true;
		camera.orthographicSize = 1.02f;
		camera.cullingMask = 1 << 9;
		camera.clearFlags = CameraClearFlags.SolidColor;
		camera.backgroundColor = new Color(0.055f, 0.062f, 0.065f);
		camera.transform.SetPositionAndRotation(panel.transform.position - panel.transform.forward * 2f, panel.transform.rotation);
		Capture(camera, Folder + "/panel-front.png", 1200, 800);
		Object.Destroy(closeup);
		Require(File.Exists(Folder + "/panel-front.png") && File.Exists(Folder + "/driver-view.png"), "Native panel and driver-camera captures");

		for (int i = 1; i <= 4; i++)
		{
			Require(health.ApplyDamage(25f, i, out _, out _, out _), "Existing damage API " + i);
			yield return null;
			int percent = 100 - 25 * i;
			Require(text.text == percent + "%" && Mathf.Abs(fill.fillAmount - percent / 100f) < 0.001f, "HP readout " + percent);
			Color color = fill.color;
			Require(i == 1 ? color.g > color.r : i == 2 ? color.r > color.g && color.g > color.b : color.r > color.g * 2f,
				"Green/orange/red threshold " + percent);
			Require(flash.alpha == 1f, "Hit border responds to successful damage");
			float until = Time.unscaledTime + 0.3f;
			while (Time.unscaledTime < until) yield return null;
			Require(flash.alpha == 0f, "Hit border clears");
		}
		control.EndDrive(user);
		yield return null;
		Require(Mathf.Abs(left.anchoredPosition.y + 81f) < 0.01f && Mathf.Abs(right.anchoredPosition.y + 81f) < 0.01f, "Exit drive resets positions");
		panel.Unbind();
		Require(content.alpha == 0f && flash.alpha == 0f, "Unbind hides stale readings");
		Require(panel.Bind(status, control), "Rebind status");
		yield return null;
		Require(text.text == "0%" && flash.alpha == 0f, "Rebind shows current HP without false hit");
	}

	private static int Capture(Camera camera, string path, int width, int height)
	{
		RenderTexture previousTarget = camera.targetTexture;
		RenderTexture previousActive = RenderTexture.active;
		var target = new RenderTexture(width, height, 24);
		var image = new Texture2D(width, height, TextureFormat.RGB24, false);
		try
		{
			camera.targetTexture = target;
			camera.Render();
			RenderTexture.active = target;
			image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
			image.Apply();
			File.WriteAllBytes(path, image.EncodeToPNG());
			int green = 0;
			foreach (Color32 pixel in image.GetPixels32())
				if (pixel.g > 100 && pixel.g > pixel.r * 1.5f && pixel.g > pixel.b * 1.3f) green++;
			return green;
		}
		finally
		{
			camera.targetTexture = previousTarget;
			RenderTexture.active = previousActive;
			target.Release();
			Object.Destroy(target); Object.Destroy(image);
		}
	}

	private static T Get<T>(Object target, string field) where T : Object => (T)new SerializedObject(target).FindProperty(field).objectReferenceValue;
	private static void Set(Object target, string field, Object value)
	{
		var data = new SerializedObject(target);
		data.FindProperty(field).objectReferenceValue = value;
		data.ApplyModifiedPropertiesWithoutUndo();
	}
	private static void Require(bool result, string message)
	{
		if (!result) throw new InvalidOperationException(message);
		log.Add("PASS: " + message);
	}
}
