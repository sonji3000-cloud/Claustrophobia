using System.Globalization;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TankMotor))]
public sealed class TankKeyboardDebug : MonoBehaviour
{
    [Tooltip("Show aiming, weapon, damage and ballistics panels alongside movement diagnostics.")]
    public bool ShowCombatDiagnostics = true;
    [SerializeField] private TankMotor motor;
    [SerializeField] private Camera observationCamera;
    [SerializeField] private TankAim aim;
    [SerializeField] private TankGun gun;
    [SerializeField] private TankHealth targetHealth;
    [SerializeField] private TankBallistics ballistics;
    [SerializeField] private Vector3 cameraOffset = new Vector3(12f, 15f, -17f);

    private Rigidbody body;
    private TankCrew crew;
    private GUIStyle valueStyle;
    // Update samples the completed physics state, independently of motor's write.
    public float CurrentForwardSpeed { get; private set; }
    public float CurrentTurnSpeed { get; private set; }

    private void Awake()
    {
        if (motor == null) motor = GetComponent<TankMotor>();
        body = GetComponent<Rigidbody>();
        crew = GetComponent<TankCrew>();
        if (aim == null) aim = GetComponent<TankAim>();
        if (gun == null) gun = GetComponent<TankGun>();
        if (ballistics == null) ballistics = GetComponent<TankBallistics>();
    }

    private void Update()
    {
        CurrentForwardSpeed = Vector3.Dot(body.linearVelocity, body.rotation * Vector3.forward);
        CurrentTurnSpeed = body.angularVelocity.y * Mathf.Rad2Deg;
    }

    private void LateUpdate()
    {
        if (observationCamera == null) return;
        observationCamera.transform.position = body.position + cameraOffset;
        observationCamera.transform.rotation = Quaternion.LookRotation(
            body.position - observationCamera.transform.position, Vector3.up);
    }

    private string GetRoleLabel()
    {
        switch (crew != null ? crew.CurrentRole : TankCrew.Role.None)
        {
            case TankCrew.Role.Driver: return "TANK / Role: DRIVER [1]";
            case TankCrew.Role.Gunner: return "TANK / Role: GUNNER [2]";
            case TankCrew.Role.Commander: return "TANK / Role: COMMANDER [3]";
            default: return "TANK / Role: NONE";
        }
    }

    private static string Signed(float value) =>
        value.ToString("+0.000;-0.000;+0.000", CultureInfo.InvariantCulture);

    private void OnGUI()
    {
        if (motor == null) return;
        if (valueStyle == null)
        {
            valueStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            valueStyle.normal.textColor = Color.white;
        }
        Matrix4x4 original = GUI.matrix;
        float scale = Mathf.Min(1.5f, Mathf.Min(Screen.width / 1410f, Screen.height / 480f));
        // Keep the tank, target and flight corridor visible above the three panels.
        float panelY = Mathf.Max(0f, Screen.height - 450f * scale - 12f);
        GUI.matrix = Matrix4x4.TRS(new Vector3(0f, panelY, 0f), Quaternion.identity,
            new Vector3(scale, scale, 1f));
        GUI.Box(new Rect(12, 12, 448, 438), GUIContent.none);
        GUILayout.BeginArea(new Rect(26, 22, 422, 418));
        GUILayout.Label(GetRoleLabel(), valueStyle);
        GUILayout.Label("Inputs and targets = last physics tick", valueStyle);
        GUILayout.Label("Left Input                       " + Signed(motor.LeftInput), valueStyle);
        GUILayout.Label("Right Input                      " + Signed(motor.RightInput), valueStyle);
        GUILayout.Label("Forward Command                  " + Signed(motor.ForwardCommand), valueStyle);
        GUILayout.Label("Turn Command                     " + Signed(motor.TurnCommand), valueStyle);
        GUILayout.Label("Target Forward Speed [m/s]       " + Signed(motor.TargetForwardSpeed), valueStyle);
        GUILayout.Label("Target Turn Speed [\u00B0/s]          " + Signed(motor.TargetTurnSpeed), valueStyle);
        GUILayout.Label("Current Forward Speed [m/s]      " + Signed(CurrentForwardSpeed), valueStyle);
        GUILayout.Label("Current Turn Speed [\u00B0/s]         " + Signed(CurrentTurnSpeed), valueStyle);
        GUILayout.Space(5);
        GUILayout.Label("Turn + = nose RIGHT / - = nose LEFT", valueStyle);
        GUILayout.Label("W/S: left +/-   I/K: right +/- (release = hold)", valueStyle);
        GUILayout.Label("Q: left neutral   U: right neutral   Space: both", valueStyle);
        GUILayout.Label("Num1 (+1,+1)  Num2 (+1,+0.3)  Num3 (+1,-1)", valueStyle);
        GUILayout.Label("Num4 (-1,+1)  Num5 (-1,-1)  Num6 (-1,-0.3)", valueStyle);
        GUILayout.EndArea();
        if (ShowCombatDiagnostics && aim != null && gun != null)
        {
            GUI.Box(new Rect(476, 12, 448, 438), GUIContent.none);
            GUILayout.BeginArea(new Rect(490, 22, 420, 418));
            GUILayout.Label("TANK / moving aim and main gun test", valueStyle);
            GUILayout.Label("Turret Input              " + Signed(aim.TurretInput), valueStyle);
            GUILayout.Label("Gun Input                 " + Signed(aim.GunInput), valueStyle);
            GUILayout.Label("Current Turret Yaw [\u00B0]    " + aim.CurrentYaw.ToString("0.000", CultureInfo.InvariantCulture), valueStyle);
            GUILayout.Label("Current Gun Pitch [\u00B0]     " + Signed(aim.CurrentPitch), valueStyle);
            GUILayout.Label("Can Fire                  " + gun.CanFire, valueStyle);
            if (!gun.CanFire) GUILayout.Label("Blocked: " + gun.FireBlockReason, valueStyle);
            GUILayout.Label("Projectile Speed [m/s]    " + Signed(gun.ProjectileSpeed), valueStyle);
            GUILayout.Label("Shot Count                " + gun.ShotCount, valueStyle);
            GUILayout.Label("Last Fire Result: " + gun.LastFireResult, valueStyle);
            GUILayout.Label("Last Successful Fire Time [s]: " + (gun.LastSuccessfulFireTime < 0f
                ? "None" : gun.LastSuccessfulFireTime.ToString("0.000", CultureInfo.InvariantCulture)), valueStyle);
            GUILayout.Space(5);
            GUILayout.Label("Yaw + = RIGHT / Pitch + = UP", valueStyle);
            GUILayout.Label("Left/Right arrows: turret / Up/Down: gun", valueStyle);
            GUILayout.Label("Enter: one shot per new press", valueStyle);
            GUILayout.Label("R: insert / T: close / Y: open EMPTY chamber", valueStyle);
            GUILayout.EndArea();
            // A third panel keeps the existing movement and aiming values visible.
            GUI.Box(new Rect(940, 12, 448, 438), GUIContent.none);
            GUILayout.BeginArea(new Rect(954, 22, 420, 418));
            GUILayout.Label("TANK / chamber and target damage", valueStyle);
            GUILayout.Label("Chamber State: " + gun.State, valueStyle);
            GUILayout.Label("Live round: " + gun.HasLiveRound + " / Can Insert: " + gun.CanInsert, valueStyle);
            GUILayout.Label("Can Fire: " + gun.CanFire, valueStyle);
            GUILayout.Label("Recover Remaining [s]: " + gun.RecoverRemaining.ToString("0.000", CultureInfo.InvariantCulture), valueStyle);
            GUILayout.Label("Last Weapon Request: " + gun.LastWeaponRequest, valueStyle);
            GUILayout.Label("Last Weapon Result:", valueStyle);
            GUILayout.Label(gun.LastWeaponResult, valueStyle);
            GUILayout.Label("Shot Damage: " + gun.ShotDamage.ToString("0.0", CultureInfo.InvariantCulture), valueStyle);
            GUILayout.Label("Successful damage hits: " + gun.DamageHitCount, valueStyle);
            if (targetHealth != null)
            {
                GUILayout.Label("Target HP: " + targetHealth.CurrentHP.ToString("0.0", CultureInfo.InvariantCulture)
                    + " / " + targetHealth.MaxHP.ToString("0.0", CultureInfo.InvariantCulture), valueStyle);
                GUILayout.Label("Target Destroyed: " + targetHealth.Destroyed, valueStyle);
            }
            GUILayout.Label("Latest impact:", valueStyle);
            GUILayout.Label(gun.LastHitSummary, valueStyle);
            GUILayout.Label("R insert -> T close -> Enter fire", valueStyle);
            GUILayout.Label("Recovering: automatic opening/ejection", valueStyle);
            GUILayout.EndArea();
        }
        GUI.matrix = original;
        if (ShowCombatDiagnostics && ballistics != null && ballistics.ShowDiagnostics)
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUI.Box(new Rect(12, 12, 448, 366), GUIContent.none);
            GUILayout.BeginArea(new Rect(26, 22, 420, 346));
            var prediction = ballistics.Current;
            var conditions = ballistics.CurrentConditions;
            GUILayout.Label("BALLISTICS / first contact preview", valueStyle);
            GUILayout.Label("Prediction: " + prediction.State, valueStyle);
            if (prediction.HasHit)
            {
                GUILayout.Label("Target: " + (prediction.Collider != null ? prediction.Collider.name : "Removed collider"), valueStyle);
                GUILayout.Label("Raw contact: " + prediction.Point.ToString("F3"), valueStyle);
                GUILayout.Label("Impact time [s]: " + prediction.HitTime.ToString("F3"), valueStyle);
            }
            else GUILayout.Label(prediction.Reason, valueStyle);
            GUILayout.Label("Speed [m/s]: " + conditions.Velocity.magnitude.ToString("F2") + " / Life [s]: " + conditions.Lifetime.ToString("F2"), valueStyle);
            GUILayout.Label("Gravity [m/s²]: " + conditions.Gravity.ToString("F2"), valueStyle);
            GUILayout.Label("Sphere radius [m]: " + conditions.Radius.ToString("F3") + " / h [s]: " + conditions.Step.ToString("F3"), valueStyle);
            GUILayout.Label("Checked [s]: " + prediction.CheckedTime.ToString("F3") + " / Steps: " + prediction.Steps, valueStyle);
            GUILayout.Label("Queries: " + prediction.Queries + " / Compute [ms]: " + ballistics.LastComputeMilliseconds.ToString("F3"), valueStyle);
            var last = gun.LastImpactComparison;
            GUILayout.Label("Last completed shot: " + (last.ShotId == 0 ? "None" : "#" + last.ShotId), valueStyle);
            if (last.ShotId != 0)
            {
                GUILayout.Label("Expected: " + last.ExpectedName + " [" + last.ExpectedColliderId + "]", valueStyle);
                GUILayout.Label("Actual: " + last.ActualName + " [" + last.ActualColliderId + "]", valueStyle);
                GUILayout.Label("Raw contact error [m]: " + (last.Comparable ? last.Error.ToString("F4") : "n/a"), valueStyle);
                GUILayout.Label(last.Result, valueStyle);
            }
            GUILayout.EndArea(); GUI.matrix = original;
        }
    }
}
