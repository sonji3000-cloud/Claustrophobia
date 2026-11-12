using System;
using System.Collections.Generic;
using System.IO;
using Fusion;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Temporary authoring tool. The saved assets work without this script.
[InitializeOnLoad]
public static class DriverStatusSetup
{
	private const string Folder = "Builds/DriverStatus";
	private const string InteriorPath = "Assets/TankPrototype/Scenes/TankInterior.unity";
	private const string WorldPath = "Assets/TankPrototype/Scenes/TankTest.unity";
	private const string TankPath = "Assets/TankPrototype/Prefabs/TigerTank.prefab";
	private const string PanelPath = "Assets/TankPrototype/Prefabs/DriverStatusPanel.prefab";
	private const string MaterialPath = "Assets/TankPrototype/Materials/DriverStatusBody.mat";
	private const string FontPath = "Assets/TankPrototype/Interaction/DriverStatusFont.asset";
	private const string PixelPath = "Assets/TankPrototype/Interaction/StatusPixel.png";
	private const string HandlePath = "Assets/TankPrototype/Interaction/StatusHandle.png";

	static DriverStatusSetup()
	{
		if (File.Exists(Path.Combine(Application.dataPath, "../" + Folder + "/prepare.request")))
			EditorApplication.delayCall += Run;
	}

	[MenuItem("Tools/Driver Status/Build Authored Panel")]
	public static void Run()
	{
		Directory.SetCurrentDirectory(Path.GetDirectoryName(Application.dataPath));
		if (!File.Exists(Folder + "/prepare.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
		Scene original = SceneManager.GetActiveScene();
		Scene interior = SceneManager.GetSceneByPath(InteriorPath);
		Scene world = SceneManager.GetSceneByPath(WorldPath);
		bool openedInterior = !interior.isLoaded;
		bool openedWorld = !world.isLoaded;
		try
		{
			for (int i = 0; i < SceneManager.sceneCount; i++)
				if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene changes; authoring stopped.");

			Directory.CreateDirectory(Folder);
			File.Delete(Folder + "/prepare.request");
			if (!File.Exists(Folder + "/TankInterior.before.unity")) File.Copy(InteriorPath, Folder + "/TankInterior.before.unity");
			if (!File.Exists(Folder + "/TankTest.before.unity")) File.Copy(WorldPath, Folder + "/TankTest.before.unity");
			if (!File.Exists(Folder + "/TigerTank.before.prefab")) File.Copy(TankPath, Folder + "/TigerTank.before.prefab");
			Sprite pixel = CreateSprite(PixelPath, false);
			Sprite handle = CreateSprite(HandlePath, true);
			TMP_FontAsset font = CreateFont();
			Material body = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
			if (body == null)
			{
				body = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
				AssetDatabase.CreateAsset(body, MaterialPath);
			}
			body.SetColor("_BaseColor", new Color(0.055f, 0.062f, 0.065f, 1f));
			EditorUtility.SetDirty(body);

			GameObject tank = PrefabUtility.LoadPrefabContents(TankPath);
			try
			{
				TankHealth health = tank.GetComponent<TankHealth>();
				if (health == null) health = tank.AddComponent<TankHealth>();
				TankStatus status = tank.GetComponent<TankStatus>();
				if (status == null) status = tank.AddComponent<TankStatus>();
				Set(status, "health", health);
				Set(status, "motor", tank.GetComponent<TankMotor>());
				PrefabUtility.SaveAsPrefabAsset(tank, TankPath);
			}
			finally { PrefabUtility.UnloadPrefabContents(tank); }

			GameObject panelAsset = CreatePanel(pixel, handle, font);
			if (openedInterior) interior = EditorSceneManager.OpenScene(InteriorPath, OpenSceneMode.Additive);
			TankInterior description = Find<TankInterior>(interior);
			if (description == null) throw new InvalidOperationException("Interior descriptor missing.");
			Transform map = null;
			foreach (GameObject root in interior.GetRootGameObjects())
				foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
					if (item.name == "DriverMap") map = item;
			if (map == null) throw new InvalidOperationException("DriverMap missing.");

			Renderer display = map.GetComponent<Renderer>();
			display.sharedMaterial = body;
			PrefabUtility.RecordPrefabInstancePropertyModifications(display);
			CrewDevice oldDevice = map.GetComponent<CrewDevice>();
			oldDevice.enabled = false;
			oldDevice.SetHighlight(false);
			PrefabUtility.RecordPrefabInstancePropertyModifications(oldDevice);
			var devices = new List<Object>();
			foreach (CrewDevice device in description.Interaction.Devices)
				if (device != null && device != oldDevice) devices.Add(device);
			SetArray(description.Interaction, "devices", devices.ToArray());
			var hud = new SerializedObject(description.Hud);
			foreach (string field in new[] { "rows", "markers" })
			{
				SerializedProperty array = hud.FindProperty(field);
				for (int i = devices.Count; i < array.arraySize; i++)
				{
					Component item = array.GetArrayElementAtIndex(i).objectReferenceValue as Component;
					if (item != null) item.gameObject.SetActive(false);
				}
			}

			TankStatusPanel placed = description.StatusPanel;
			if (placed != null) Object.DestroyImmediate(placed.gameObject);
			placed = ((GameObject)PrefabUtility.InstantiatePrefab(panelAsset, interior)).GetComponent<TankStatusPanel>();
			Transform anchor = new SerializedObject(description.Seats).FindProperty("driver").objectReferenceValue as Transform;
			if (anchor == null) throw new InvalidOperationException("Driver anchor missing.");
			Vector3 localEye = map.InverseTransformPoint(anchor.position);
			float side = localEye.x >= 0f ? 1f : -1f;
			Vector3 normal = map.TransformDirection(Vector3.right * side).normalized;
			Vector3 size = map.lossyScale;
			placed.transform.SetParent(map.parent, true);
			placed.transform.SetPositionAndRotation(map.TransformPoint(new Vector3(side * 0.5f, 0f, 0f)) + normal * 0.015f,
				Quaternion.LookRotation(-normal, map.up));
			float scale = Mathf.Min(Mathf.Abs(size.z) / 900f, Mathf.Abs(size.y) / 600f) * 0.95f;
			placed.transform.localScale = Vector3.one * scale;
			PrefabUtility.RecordPrefabInstancePropertyModifications(placed.transform);
			Set(description, "statusPanel", placed);
			EditorSceneManager.MarkSceneDirty(interior);
			if (!EditorSceneManager.SaveScene(interior)) throw new IOException("Interior save failed.");

			if (openedWorld) world = EditorSceneManager.OpenScene(WorldPath, OpenSceneMode.Additive);
			TankSceneLink link = Find<TankSceneLink>(world);
			if (link == null) throw new InvalidOperationException("World scene link missing.");
			TankControl control = new SerializedObject(link).FindProperty("control").objectReferenceValue as TankControl;
			TankStatus playerStatus = control.GetComponent<TankStatus>();
			if (playerStatus == null) throw new InvalidOperationException("Player status did not propagate from prefab.");
			Set(link, "status", playerStatus);
			NetworkObject entity = control.GetComponent<NetworkObject>();
			var network = new SerializedObject(entity);
			SerializedProperty behaviours = network.FindProperty("NetworkedBehaviours");
			bool registered = false;
			for (int i = 0; i < behaviours.arraySize; i++)
				registered |= behaviours.GetArrayElementAtIndex(i).objectReferenceValue == playerStatus;
			if (!registered)
			{
				int index = behaviours.arraySize++;
				behaviours.GetArrayElementAtIndex(index).objectReferenceValue = playerStatus;
				network.ApplyModifiedPropertiesWithoutUndo();
				PrefabUtility.RecordPrefabInstancePropertyModifications(entity);
			}
			EditorSceneManager.MarkSceneDirty(world);
			if (!EditorSceneManager.SaveScene(world)) throw new IOException("World save failed.");
			AssetDatabase.SaveAssets();
			File.WriteAllText(Folder + "/prepare-result.txt", "PASS: DriverMap charcoal material; authored panel; player HP/status; scene bindings.\n"
				+ "Panel world position=" + placed.transform.position + "; normal=" + normal + "; uniform scale=" + scale
				+ "\nSelectable devices=" + devices.Count + "; network behaviours=" + behaviours.arraySize);
		}
		catch (Exception e) { File.WriteAllText(Folder + "/prepare-error.txt", e.ToString()); Debug.LogException(e); }
		finally
		{
			if (openedWorld && world.isLoaded) EditorSceneManager.CloseScene(world, true);
			if (openedInterior && interior.isLoaded) EditorSceneManager.CloseScene(interior, true);
			if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
		}
	}

	private static GameObject CreatePanel(Sprite pixel, Sprite handle, TMP_FontAsset font)
	{
		var root = new GameObject("DriverStatusPanel", typeof(TankStatusPanel));
		root.layer = 9;
		var display = new GameObject("Display", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
		display.layer = 9;
		display.transform.SetParent(root.transform, false);
		var rect = display.GetComponent<RectTransform>();
		rect.anchoredPosition3D = Vector3.zero;
		rect.sizeDelta = new Vector2(900f, 600f);
		display.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
		display.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
		CanvasGroup content = display.GetComponent<CanvasGroup>();
		content.interactable = content.blocksRaycasts = false;
		Image track = Image("HealthTrack", display.transform, pixel, new Vector2(-99f, 150f), new Vector2(558f, 42f), new Color(0.15f, 0.18f, 0.18f, 1f));
		Image fill = Image("HealthFill", display.transform, pixel, track.rectTransform.anchoredPosition, track.rectTransform.sizeDelta, new Color(0.24f, 0.75f, 0.36f, 1f));
		fill.type = UnityEngine.UI.Image.Type.Filled;
		fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
		fill.fillOrigin = 0;
		fill.fillAmount = 1f;
		var label = new GameObject("HealthPercent", typeof(RectTransform), typeof(TextMeshProUGUI));
		label.layer = 9;
		label.transform.SetParent(display.transform, false);
		var text = label.GetComponent<TextMeshProUGUI>();
		text.font = font;
		text.fontSize = 56f;
		text.fontStyle = FontStyles.Normal;
		text.alignment = TextAlignmentOptions.MidlineRight;
		text.color = new Color(0.9f, 0.93f, 0.91f, 1f);
		text.raycastTarget = false;
		text.textWrappingMode = TextWrappingModes.NoWrap;
		text.enableAutoSizing = false;
		text.text = "100%";
		text.rectTransform.sizeDelta = new Vector2(162f, 80f);
		text.rectTransform.anchoredPosition = new Vector2(306f, 150f);
		var rails = new Color(0.37f, 0.41f, 0.4f, 1f);
		var knobs = new Color(0.86f, 0.9f, 0.87f, 1f);
		Image("LeftSlot", display.transform, pixel, new Vector2(-162f, -81f), new Vector2(10f, 246f), rails);
		Image("RightSlot", display.transform, pixel, new Vector2(162f, -81f), new Vector2(10f, 246f), rails);
		Image left = Image("LeftHandle", display.transform, handle, new Vector2(-162f, -81f), new Vector2(72f, 24f), knobs);
		Image right = Image("RightHandle", display.transform, handle, new Vector2(162f, -81f), new Vector2(72f, 24f), knobs);
		left.type = right.type = UnityEngine.UI.Image.Type.Sliced;
		var border = new GameObject("HitBorder", typeof(RectTransform), typeof(CanvasGroup));
		border.layer = 9;
		border.transform.SetParent(display.transform, false);
		border.GetComponent<RectTransform>().sizeDelta = rect.sizeDelta;
		CanvasGroup flash = border.GetComponent<CanvasGroup>();
		flash.alpha = 0f;
		flash.interactable = flash.blocksRaycasts = false;
		Color red = new Color(0.9f, 0.2f, 0.16f, 0.85f);
		Image("Top", border.transform, pixel, new Vector2(0f, 293f), new Vector2(886f, 6f), red);
		Image("Bottom", border.transform, pixel, new Vector2(0f, -293f), new Vector2(886f, 6f), red);
		Image("Left", border.transform, pixel, new Vector2(-440f, 0f), new Vector2(6f, 580f), red);
		Image("Right", border.transform, pixel, new Vector2(440f, 0f), new Vector2(6f, 580f), red);
		TankStatusPanel panel = root.GetComponent<TankStatusPanel>();
		Set(panel, "content", content); Set(panel, "hpFill", fill); Set(panel, "hpText", text);
		Set(panel, "leftHandle", left.rectTransform); Set(panel, "rightHandle", right.rectTransform); Set(panel, "hitBorder", flash);
		GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PanelPath);
		Object.DestroyImmediate(root);
		return saved;
	}

	private static Image Image(string name, Transform parent, Sprite sprite, Vector2 pos, Vector2 size, Color color)
	{
		var item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		item.layer = 9;
		item.transform.SetParent(parent, false);
		Image image = item.GetComponent<Image>();
		image.sprite = sprite;
		image.color = color;
		image.raycastTarget = false;
		image.rectTransform.anchoredPosition = pos;
		image.rectTransform.sizeDelta = size;
		return image;
	}

	private static Sprite CreateSprite(string path, bool rounded)
	{
		var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
		for (int y = 0; y < 16; y++)
			for (int x = 0; x < 16; x++)
			{
				float dx = Mathf.Max(4f - (x + 0.5f), 0f, x + 0.5f - 12f);
				float dy = Mathf.Max(4f - (y + 0.5f), 0f, y + 0.5f - 12f);
				float alpha = rounded ? Mathf.Clamp01(4.5f - Mathf.Sqrt(dx * dx + dy * dy)) : 1f;
				texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
			}
		File.WriteAllBytes(path, texture.EncodeToPNG());
		Object.DestroyImmediate(texture);
		AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
		var importer = (TextureImporter)AssetImporter.GetAtPath(path);
		importer.textureType = TextureImporterType.Sprite;
		importer.spriteImportMode = SpriteImportMode.Single;
		importer.mipmapEnabled = false;
		importer.textureCompression = TextureImporterCompression.Uncompressed;
		if (rounded) importer.spriteBorder = new Vector4(4f, 4f, 4f, 4f);
		importer.SaveAndReimport();
		return AssetDatabase.LoadAssetAtPath<Sprite>(path);
	}

	private static TMP_FontAsset CreateFont()
	{
		const string settingsPath = "Assets/TankPrototype/Resources/TMP Settings.asset";
		if (!AssetDatabase.IsValidFolder("Assets/TankPrototype/Resources"))
			AssetDatabase.CreateFolder("Assets/TankPrototype", "Resources");
		TMP_Settings settings = Resources.Load<TMP_Settings>("TMP Settings");
		if (settings == null)
		{
			settings = ScriptableObject.CreateInstance<TMP_Settings>();
			AssetDatabase.CreateAsset(settings, settingsPath);
			var defaults = new SerializedObject(settings);
			defaults.FindProperty("assetVersion").stringValue = "2";
			defaults.FindProperty("m_defaultFontSize").floatValue = 36f;
			defaults.FindProperty("m_defaultAutoSizeMinRatio").floatValue = 0.5f;
			defaults.FindProperty("m_defaultAutoSizeMaxRatio").floatValue = 2f;
			defaults.ApplyModifiedPropertiesWithoutUndo();
			AssetDatabase.SaveAssets();
		}
		TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
		if (asset != null) return asset;
		Font source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Photon/Fusion/Runtime/RuntimeAssets/JetBrainsMono-Regular.ttf");
		asset = TMP_FontAsset.CreateFontAsset(source, 64, 8, GlyphRenderMode.SDFAA, 512, 512);
		if (asset == null) throw new InvalidOperationException("Status font creation failed.");
		asset.name = "DriverStatusFont";
		var chars = new System.Text.StringBuilder();
		for (int i = 32; i < 127; i++) chars.Append((char)i);
		asset.TryAddCharacters(chars.ToString(), out string missing);
		if (!string.IsNullOrEmpty(missing)) throw new InvalidOperationException("Missing font glyphs: " + missing);
		asset.atlasPopulationMode = AtlasPopulationMode.Static;
		AssetDatabase.CreateAsset(asset, FontPath);
		AssetDatabase.AddObjectToAsset(asset.material, asset);
		foreach (Texture2D atlas in asset.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, asset);
		EditorUtility.SetDirty(asset);
		var settingsData = new SerializedObject(settings);
		settingsData.FindProperty("m_defaultFontAsset").objectReferenceValue = asset;
		settingsData.ApplyModifiedPropertiesWithoutUndo();
		return asset;
	}

	private static T Find<T>(Scene scene) where T : Component
	{
		foreach (GameObject root in scene.GetRootGameObjects())
		{
			T item = root.GetComponentInChildren<T>(true);
			if (item != null) return item;
		}
		return null;
	}

	private static void Set(Object target, string field, Object value)
	{
		var data = new SerializedObject(target);
		data.FindProperty(field).objectReferenceValue = value;
		data.ApplyModifiedPropertiesWithoutUndo();
		PrefabUtility.RecordPrefabInstancePropertyModifications(target);
	}

	private static void SetArray(Object target, string field, Object[] values)
	{
		var data = new SerializedObject(target);
		SerializedProperty array = data.FindProperty(field);
		array.arraySize = values.Length;
		for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
		data.ApplyModifiedPropertiesWithoutUndo();
		PrefabUtility.RecordPrefabInstancePropertyModifications(target);
	}
}
