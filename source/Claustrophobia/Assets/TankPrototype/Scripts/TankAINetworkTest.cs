using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

// TankTest-only controls. Uses the existing session; every command is consumed on the host tick.
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class TankAINetworkTest : NetworkBehaviour
{
	public enum TestCommand { None, ResetEnemy, Patrol, Stop, Attack }
	public enum TestState { Empty, Stopped, Patrolling, Attacking, Destroyed, TargetDown }

	[SerializeField] private SessionJoinInit session;
	[SerializeField] private NetworkObject enemyPrefab;
	[SerializeField] private NetworkObject targetPrefab;
	[SerializeField] private Transform spawnPoint;
	[SerializeField] private Transform[] waypoints = new Transform[4];
	[SerializeField] private Transform targetSpawnPoint;
	[SerializeField] private Camera previewCamera;
	[SerializeField, Min(0f)] private float cornerPause = 0.1f;
	[SerializeField, Min(0.1f)] private float patrolArrivalRadius = 1f;
	[SerializeField] private bool fastTestMovement = true;

	[Networked] public NetworkObject EnemyObject { get; private set; }
	[Networked] public NetworkObject TargetObject { get; private set; }
	[Networked] public TestState State { get; private set; }
	[Networked] public int Corner { get; private set; }
	[Networked] public int Laps { get; private set; }
	[Networked] public TankAI.MoveState MoveStatus { get; private set; }
	[Networked] public TankAI.AttackState AttackStatus { get; private set; }

	private TestCommand pending;
	private TickTimer pause;
	private readonly List<NetworkObject> cleanupObjects = new List<NetworkObject>(16);
	private bool waiting;
	private bool lapStarted;
	private int routeIndex;
	private TickTimer motionCheck;
	private bool motionCheckPending;
	private Vector3 motionStart;
	private Vector3 motionForwardStart;
	private int motionAITicks, motionMotorTicks;
	private string motionWarning = "";
	private NetworkObject previewEnemy;
	private NetworkObject ownedEnemy;
	private NetworkObject ownedTarget;
	private NetworkObject cachedEnemy;
	private NetworkObject cachedTarget;
	private TankAI ai;
	private TankAim aim;
	private TankGun gun;
	private TankHealth health;
	private TankMotor motor;
	private Rigidbody body;
	private TankHealth targetHealth;
	private Transform targetPoint;
	private string message = "연결 후 호스트가 적을 생성하세요.";
	private bool expanded = true;
	private bool showPreview;
	private RenderTexture preview;
	private Font font;
	private GUIStyle labelStyle, buttonStyle;

	private bool Valid => Object != null && Object.IsValid && Runner != null && Runner.IsRunning;
	public bool IsHost => Valid && Runner.IsServer && HasStateAuthority;
	public bool IsReady => Valid && session != null && session.IsSceneReady && session.Runner == Runner;
	public bool CanCommand => isActiveAndEnabled && IsHost && IsReady;

	public override void Spawned()
	{
		pending = TestCommand.None;
		waiting = lapStarted = false;
		if (!IsHost) return;
		EnemyObject = null;
		TargetObject = null;
		State = TestState.Empty;
		Corner = Laps = 0;
		MoveStatus = TankAI.MoveState.Idle;
		AttackStatus = TankAI.AttackState.Idle;
	}

	// Called by the Game/exe panel; never executes AI or starts a second runner here.
	public bool Request(TestCommand command)
	{
		if (!CanCommand || (command != TestCommand.ResetEnemy && command != TestCommand.Patrol
			&& command != TestCommand.Stop && command != TestCommand.Attack)) return false;
		if (command != TestCommand.ResetEnemy && (EnemyObject == null || !EnemyObject.IsValid))
		{
			message = "먼저 적을 생성하세요.";
			return false;
		}
		pending = command;
		return true;
	}

	public override void FixedUpdateNetwork()
	{
		if (!isActiveAndEnabled || !IsHost) return;
		if (!IsReady)
		{
			pending = TestCommand.None;
			StopHost();
			return;
		}

		CacheEnemy();
		TestCommand command = pending;
		pending = TestCommand.None;
		switch (command)
		{
			case TestCommand.ResetEnemy: ResetEnemy(); break;
			case TestCommand.Patrol: StartPatrol(); break;
			case TestCommand.Stop:
				StopHost(); message = "이동·공격을 정지했습니다."; break;
			case TestCommand.Attack: StartAttack(); break;
		}

		if (ai == null || EnemyObject == null || !EnemyObject.IsValid)
		{
			waiting = false;
			State = TestState.Empty;
			MoveStatus = TankAI.MoveState.Idle;
			AttackStatus = TankAI.AttackState.Idle;
			return;
		}
		if (!ai.CanAct)
		{
			StopHost();
			State = health != null && health.Destroyed ? TestState.Destroyed : TestState.Stopped;
			message = State == TestState.Destroyed ? "적 전차가 파괴되어 동작을 중단했습니다." : "적 전차가 행동할 수 없는 상태입니다.";
		}
		else if (State == TestState.Patrolling) TickPatrol();
		else if (State == TestState.Attacking)
		{
			if (ai.AttackStatus == TankAI.AttackState.TargetDown)
			{
				State = TestState.TargetDown;
				message = "표적을 파괴했습니다. 초기화하면 다시 시험할 수 있습니다.";
			}
			else if (ai.AttackStatus == TankAI.AttackState.Idle) State = TestState.Stopped;
		}

		MoveStatus = ai.MoveStatus;
		AttackStatus = ai.AttackStatus;
	}

	private void ResetEnemy()
	{
		if (enemyPrefab == null || spawnPoint == null || enemyPrefab.GetComponent<TankAI>() == null
			|| targetPrefab == null || targetSpawnPoint == null || targetPrefab.GetComponent<TankHealth>() == null
			|| targetPrefab.transform.Find("Aim Point") == null)
		{
			message = "적·표적 프리팹 또는 생성 지점이 없습니다.";
			return;
		}
		StopHost();
		RemoveOwnedObjects(Runner);
		EnemyObject = null;
		TargetObject = null;
		CacheEnemy();
		CacheTarget();
		State = TestState.Empty;
		Corner = Laps = 0;
		try
		{
			ownedTarget = Runner.Spawn(targetPrefab, targetSpawnPoint.position, targetSpawnPoint.rotation);
			if (ownedTarget == null) throw new InvalidOperationException("Target spawn returned null");
			TargetObject = ownedTarget;
			CacheTarget();
			ownedEnemy = Runner.Spawn(enemyPrefab, spawnPoint.position, spawnPoint.rotation);
			if (ownedEnemy == null) throw new InvalidOperationException("Spawn returned null");
			EnemyObject = ownedEnemy;
			CacheEnemy();
			if (fastTestMovement && motor != null)
			{
				motor.MaxMoveSpeed = 6f;
				motor.MoveAcceleration = 8f;
				motor.MoveDeceleration = 10f;
				motor.MaxTurnSpeed = 120f;
				motor.TurnAcceleration = motor.TurnDeceleration = 360f;
			}
			State = TestState.Stopped;
			message = "적 생성 완료. 순찰 또는 공격을 누르세요.";
		}
		catch (Exception e)
		{
			RemoveOwnedObjects(Runner);
			EnemyObject = TargetObject = null;
			CacheEnemy(); CacheTarget();
			State = TestState.Empty;
			message = "적 생성 실패: " + e.Message;
			Debug.LogError("[AI NETWORK TEST] " + message, this);
		}
	}

	private bool HasRoute()
	{
		if (waypoints == null || waypoints.Length != 4) return false;
		for (int i = 0; i < waypoints.Length; i++) if (waypoints[i] == null) return false;
		return true;
	}

	private void StartPatrol()
	{
		if (ai == null || !ai.CanAct || !HasRoute())
		{
			message = "살아 있는 적과 순찰 지점 4개가 필요합니다.";
			return;
		}
		int first = DistanceTo(0) <= patrolArrivalRadius ? 1 : 0;
		if (!MoveToCorner(first))
		{
			message = "이동 명령 거부: " + ai.MoveBlockReason;
			Debug.LogWarning("[AI PATROL] " + message, this);
			return;
		}
		lapStarted = first == 1;
		Laps = 0;
		State = TestState.Patrolling;
		message = "사각 순찰 중. 각 지점에서 잠깐 정지합니다.";
	}

	private void TickPatrol()
	{
		if (!HasRoute())
		{
			StopHost();
			message = "순찰이 중단되었습니다.";
			return;
		}
		if (waiting)
		{
			if (!pause.Expired(Runner)) return;
			int next = (routeIndex + 1) % waypoints.Length;
			if (!MoveToCorner(next))
			{
				StopHost();
				message = "이동 명령 거부: " + ai.MoveBlockReason;
				return;
			}
			lapStarted = true;
			return;
		}
		float distance = DistanceTo(routeIndex);
		if (distance <= patrolArrivalRadius)
		{
			// Judge this route point by the actual body position, then stop explicitly.
			// Do not wait indefinitely for the AI's stricter velocity/rotation arrival test.
			ai.StopMove();
			motionWarning = "";
			if (routeIndex == 0 && lapStarted) Laps++;
			waiting = true;
			pause = TickTimer.CreateFromSeconds(Runner, cornerPause);
			return;
		}
		if (ai.MoveStatus == TankAI.MoveState.Idle)
		{
			StopHost();
			message = "이동 명령이 취소되어 순찰을 멈췄습니다.";
			return;
		}
		if (ai.MoveStatus == TankAI.MoveState.Arrived && !ai.MoveTo(waypoints[routeIndex].position))
		{
			StopHost();
			message = "이동 명령 거부: " + ai.MoveBlockReason;
			return;
		}
		if (motionCheckPending && motionCheck.Expired(Runner))
		{
			Vector3 moved = body.position - motionStart;
			moved.y = 0f;
			float turned = Vector3.Angle(motionForwardStart, body.rotation * Vector3.forward);
			string warning = "";
			// A tank intentionally turns in place before driving. Rotation is progress too.
			if (moved.sqrMagnitude < 0.01f && turned < 5f)
			{
				// The destination can be issued just before the first AI/motor update of this window.
				if (ai.TickCount - motionAITicks <= 1) warning = "AI 판단 갱신이 멈췄습니다.";
				else if (motor.PhysicsTickCount - motionMotorTicks <= 1) warning = "차체 구동 갱신이 멈췄습니다.";
				else if (Mathf.Abs(motor.TargetForwardSpeed) < 0.01f && Mathf.Abs(motor.TargetTurnSpeed) < 0.1f)
					warning = "이동 중인데 구동 입력이 없습니다.";
				else warning = "구동 명령이 있지만 차체가 멈춰 있습니다.";
			}
			if (warning != "" && warning != motionWarning)
				Debug.LogWarning("[AI PATROL STALLED] " + warning + " " + MotionReport(), this);
			motionWarning = warning;
			BeginMotionCheck();
		}
	}

	private float DistanceTo(int index)
	{
		if (body == null) return float.PositiveInfinity;
		Vector3 delta = waypoints[index].position - body.position;
		delta.y = 0f;
		return delta.magnitude;
	}

	private bool MoveToCorner(int index)
	{
		if (!ai.MoveTo(waypoints[index].position)) return false;
		routeIndex = Corner = index;
		waiting = false;
		pause = TickTimer.None;
		motionWarning = "";
		BeginMotionCheck();
		Debug.Log("[AI PATROL] " + MotionReport(), this);
		return true;
	}

	private void BeginMotionCheck()
	{
		motionStart = body.position;
		motionForwardStart = body.rotation * Vector3.forward;
		motionAITicks = ai.TickCount;
		motionMotorTicks = motor.PhysicsTickCount;
		motionCheck = TickTimer.CreateFromSeconds(Runner, 3f);
		motionCheckPending = true;
	}

	private float HeadingError()
	{
		if (body == null || !HasRoute()) return 0f;
		Vector3 delta = waypoints[routeIndex].position - body.position;
		delta.y = 0f;
		return Vector3.SignedAngle(body.rotation * Vector3.forward, delta, Vector3.up);
	}

	private string MotionReport()
	{
		return "point=" + (routeIndex + 1) + " goal=" + waypoints[routeIndex].position.ToString("F1")
			+ " body=" + body.position.ToString("F2") + " velocity=" + body.linearVelocity.ToString("F2")
			+ " yawSpeed=" + (body.angularVelocity.y * Mathf.Rad2Deg).ToString("F1")
			+ " headingError=" + HeadingError().ToString("F1")
			+ " aiInputs=" + ai.LeftDriveInput.ToString("F2") + "," + ai.RightDriveInput.ToString("F2")
			+ " motorInputs=" + motor.LeftInput.ToString("F2") + "," + motor.RightInput.ToString("F2")
			+ " targetSpeeds=" + motor.TargetForwardSpeed.ToString("F2") + "," + motor.TargetTurnSpeed.ToString("F1")
			+ " appliedSpeeds=" + motor.AppliedForwardSpeed.ToString("F2") + "," + motor.AppliedTurnSpeed.ToString("F1")
			+ " aiTicks=" + ai.TickCount + " physicsTicks=" + motor.PhysicsTickCount
			+ " kinematic=" + body.isKinematic + " constraints=" + body.constraints;
	}

	private void StartAttack()
	{
		CacheTarget();
		if (ai == null || !ai.Attack(targetPoint))
		{
			message = "공격 명령 거부: 적과 지정 표적이 살아 있는지 확인하세요.";
			return;
		}
		waiting = false;
		pause = TickTimer.None;
		motionWarning = "";
		State = TestState.Attacking;
		message = "고정 시험 표적을 공격합니다.";
	}

	private void StopHost()
	{
		waiting = lapStarted = false;
		pause = TickTimer.None;
		motionCheck = TickTimer.None;
		motionCheckPending = false;
		motionWarning = "";
		if (ai != null) ai.StopActions();
		MoveStatus = ai != null ? ai.MoveStatus : TankAI.MoveState.Idle;
		AttackStatus = ai != null ? ai.AttackStatus : TankAI.AttackState.Idle;
		State = EnemyObject != null && EnemyObject.IsValid ? TestState.Stopped : TestState.Empty;
	}

	private void CacheEnemy()
	{
		NetworkObject observed = EnemyObject;
		if (observed != null && !observed.IsValid) observed = null;
		if (observed == cachedEnemy) return;
		cachedEnemy = observed;
		ai = observed != null ? observed.GetComponent<TankAI>() : null;
		aim = observed != null ? observed.GetComponent<TankAim>() : null;
		gun = observed != null ? observed.GetComponent<TankGun>() : null;
		health = observed != null ? observed.GetComponent<TankHealth>() : null;
		motor = observed != null ? observed.GetComponent<TankMotor>() : null;
		body = observed != null ? observed.GetComponent<Rigidbody>() : null;
	}

	private void CacheTarget()
	{
		NetworkObject observed = TargetObject;
		if (observed != null && !observed.IsValid) observed = null;
		if (observed == cachedTarget) return;
		cachedTarget = observed;
		targetHealth = observed != null ? observed.GetComponent<TankHealth>() : null;
		targetPoint = observed != null ? observed.transform.Find("Aim Point") : null;
	}

	private void RemoveOwnedObjects(NetworkRunner runner)
	{
		if (ownedEnemy != null && ownedEnemy.IsValid && ownedEnemy.Runner == runner)
		{
			TankGun weapon = ownedEnemy.GetComponent<TankGun>();
			if (weapon != null)
			{
				runner.GetAllNetworkObjects(cleanupObjects);
				foreach (NetworkObject obj in cleanupObjects)
				{
					if (obj == null || !obj.IsValid || obj.Runner != runner || !obj.HasStateAuthority) continue;
					TankProjectile projectile = obj.GetComponent<TankProjectile>();
					if (projectile != null && projectile.SourceGun == weapon) projectile.Remove();
				}
				cleanupObjects.Clear();
			}
			runner.Despawn(ownedEnemy);
		}
		ownedEnemy = null;
		if (ownedTarget != null && ownedTarget.IsValid && ownedTarget.Runner == runner)
			runner.Despawn(ownedTarget);
		ownedTarget = null;
	}

	public override void Despawned(NetworkRunner runner, bool hasState)
	{
		pending = TestCommand.None;
		waiting = false;
		if (runner != null && runner.IsRunning && runner.IsServer) RemoveOwnedObjects(runner);
	}

	private void OnDisable()
	{
		pending = TestCommand.None;
		if (IsHost) StopHost();
		if (previewCamera != null) previewCamera.enabled = false;
	}

	private void OnEnable()
	{
		if (previewCamera != null) previewCamera.enabled = showPreview && expanded;
	}

	private void SetPreview(bool visible)
	{
		if (previewCamera == null) return;
		showPreview = visible;
		if (visible && preview == null)
		{
			preview = new RenderTexture(640, 400, 24) { name = "AI Network Test Preview" };
			preview.Create();
			previewCamera.targetTexture = preview;
		}
		previewCamera.enabled = visible && expanded;
	}

	public override void Render()
	{
		if (!Valid || EnemyObject == null || !EnemyObject.IsValid || previewEnemy == EnemyObject) return;
		previewEnemy = EnemyObject;
		SetPreview(true);
	}

	private void OnGUI()
	{
		if (!isActiveAndEnabled) return;
		if (labelStyle == null)
		{
			font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 14);
			labelStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, wordWrap = true };
			buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14 };
		}
		float scale = Mathf.Min(1f, Mathf.Min(Screen.width / 900f, Screen.height / 470f));
		if (scale <= 0f) return;
		Matrix4x4 previous = GUI.matrix;
		GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
		float x = Mathf.Max(8f, Screen.width / scale - 356f);
		GUI.Box(new Rect(x, 12f, 344f, expanded ? 430f : 30f), GUIContent.none);
		GUILayout.BeginArea(new Rect(x + 8f, 14f, 328f, expanded ? 426f : 28f));
		if (GUILayout.Button(expanded ? "적 AI 네트워크 시험  ▴" : "적 AI 네트워크 시험  ▾", buttonStyle))
		{
			expanded = !expanded;
			if (previewCamera != null) previewCamera.enabled = showPreview && expanded;
		}
		if (expanded)
		{
			GUILayout.Label(!IsReady ? "세션 연결 대기" : IsHost ? "HOST · 조작 가능" : "CLIENT · 관찰 전용", labelStyle);
			bool savedEnabled = GUI.enabled;
			GUI.enabled = savedEnabled && CanCommand;
			if (GUILayout.Button("적 생성 / 초기화", buttonStyle)) Request(TestCommand.ResetEnemy);
			GUI.enabled = savedEnabled && CanCommand && EnemyObject != null && EnemyObject.IsValid;
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("사각 순찰 시작", buttonStyle)) Request(TestCommand.Patrol);
			if (GUILayout.Button("전체 정지", buttonStyle)) Request(TestCommand.Stop);
			GUILayout.EndHorizontal();
			if (GUILayout.Button("지정 표적 공격", buttonStyle)) Request(TestCommand.Attack);
			GUI.enabled = savedEnabled;
			if (Valid)
			{
				CacheEnemy();
				CacheTarget();
				GUILayout.Label("상태: " + State + "  /  지점 " + (Corner + 1) + "  /  " + Laps + "바퀴", labelStyle);
				if (cachedEnemy != null)
				{
					GUILayout.Label("적 ID: " + cachedEnemy.Id + "  위치: " + cachedEnemy.transform.position.ToString("F1"), labelStyle);
					if (aim != null && gun != null)
						GUILayout.Label("포탑 " + aim.CurrentYaw.ToString("F1") + "° / 포신 " + aim.CurrentPitch.ToString("F1")
							+ "° / 발사 " + gun.ShotCount, labelStyle);
					GUILayout.Label("이동 " + MoveStatus + " / 공격 " + AttackStatus, labelStyle);
					if (IsHost && health != null)
					{
						if (State == TestState.Patrolling)
							GUILayout.Label("목표 " + (routeIndex + 1) + " · 남은 " + DistanceTo(routeIndex).ToString("F1")
								+ "m · " + (waiting ? "코너 대기" : Mathf.Abs(HeadingError()) >= 45f ? "방향 전환" : "이동"), labelStyle);
						GUILayout.Label("속도 " + new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude.ToString("F2")
							+ "m/s / 명령 " + motor.TargetForwardSpeed.ToString("F2") + "m/s", labelStyle);
						GUILayout.Label("회전 " + (body.angularVelocity.y * Mathf.Rad2Deg).ToString("F1")
							+ "°/s / 명령 " + motor.TargetTurnSpeed.ToString("F1") + "°/s", labelStyle);
						GUILayout.Label("갱신: AI " + ai.TickCount + "회 / 구동 " + motor.PhysicsTickCount
							+ "회" + (body.isKinematic ? " · Kinematic" : ""), labelStyle);
						GUILayout.Label("호스트 HP: 적 " + health.CurrentHP.ToString("F0")
							+ " / 표적 " + (targetHealth != null ? targetHealth.CurrentHP.ToString("F0") : "없음"), labelStyle);
					}
				}
			}
			string detail = IsHost && ai != null && ai.AttackStatus == TankAI.AttackState.Blocked
				? ai.AttackBlockReason : motionWarning != "" ? motionWarning : message;
			GUILayout.Label(!IsReady && session != null && !string.IsNullOrEmpty(session.LastError)
				? session.LastError : IsHost ? detail : "호스트의 적을 관찰합니다. HP 공유는 별도 연결.", labelStyle);
			GUI.enabled = savedEnabled && previewCamera != null;
			if (GUILayout.Button(showPreview ? "시험 구역 화면 닫기" : "시험 구역 화면 보기", buttonStyle)) SetPreview(!showPreview);
			GUI.enabled = savedEnabled;
		}
		GUILayout.EndArea();
		if (expanded && showPreview && preview != null)
		{
			float width = Mathf.Min(640f, x - 24f);
			float height = width * 0.625f;
			Rect view = new Rect(x - width - 12f, 36f, width, height);
			GUI.Box(new Rect(view.x - 4f, 12f, width + 8f, height + 32f), GUIContent.none);
			GUI.Label(new Rect(view.x + 4f, 14f, width - 8f, 20f), "시험 구역 · 순찰 1 → 2 → 3 → 4 → 1", labelStyle);
			GUI.DrawTexture(view, preview, ScaleMode.ScaleToFit);
			if (HasRoute())
				for (int i = 0; i < waypoints.Length; i++)
				{
					Vector3 p = previewCamera.WorldToViewportPoint(waypoints[i].position);
					if (p.z > 0f && p.x >= 0f && p.x <= 1f && p.y >= 0f && p.y <= 1f)
						GUI.Box(new Rect(view.x + p.x * view.width - 10f, view.y + (1f - p.y) * view.height - 10f, 22f, 22f), (i + 1).ToString());
				}
		}
		GUI.matrix = previous;
	}

	private void OnDestroy()
	{
		if (previewCamera != null)
		{
			previewCamera.enabled = false;
			previewCamera.targetTexture = null;
		}
		if (preview != null) { preview.Release(); Destroy(preview); }
		if (font != null) Destroy(font);
	}

	private void OnValidate()
	{
		if (float.IsNaN(cornerPause) || float.IsInfinity(cornerPause) || cornerPause < 0f) cornerPause = 0.1f;
		if (float.IsNaN(patrolArrivalRadius) || float.IsInfinity(patrolArrivalRadius) || patrolArrivalRadius < 0.1f) patrolArrivalRadius = 1f;
	}
}
