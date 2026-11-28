using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class LobbyInput : MonoBehaviour
{
	[SerializeField] private string gameScenePath;

	private void Update()
	{
		Keyboard keyboard = Keyboard.current;
		if (keyboard == null || !keyboard.mKey.wasPressedThisFrame) return;

		int sceneIndex = SceneUtility.GetBuildIndexByScenePath(gameScenePath);
		if (sceneIndex < 0)
		{
			Debug.LogError($"The lobby's game scene is missing from Build Settings: {gameScenePath}", this);
			return;
		}

		if (SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Single) != null)
			enabled = false;
	}
}
