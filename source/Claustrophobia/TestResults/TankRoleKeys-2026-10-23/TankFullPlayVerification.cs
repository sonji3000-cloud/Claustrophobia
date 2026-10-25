using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Temporary verification runner. Removed after the recorded Play Mode run.
public static class TankFullPlayVerification
{
    [Serializable] public class CheckResult { public string name, details; public bool passed; }
    [Serializable] public class Report
    {
        public string status = "running", phase, unityVersion, inputPath, scene;
        public bool focused;
        public List<CheckResult> checks = new List<CheckResult>();
        public List<string> screenshots = new List<string>();
        public List<string> errors = new List<string>();
    }
    static readonly string output = Path.GetFullPath("TestResults/TankRoleKeys-2026-10-23");
    static Report report;
    static bool running;
    static Keyboard keyboard, previousKeyboard;
    static Mouse mouse, previousMouse;
    static TankInput input;
    static TankControl control;
    static TankCrew crew;
    static TankCrewCameras view;
    static TankAim aim;
    static TankGun gun;
    static TankMotor motor;
    static TankTrackAnimation tracks;
    static TankGunRecoil recoil;
    static TankBallistics ballistics;
    static Rigidbody body;
    static GameObject tiger, basic;
    static TankHealth target;
    static Vector3 origin;
    static Quaternion rotation;
    static Scene testScene;

    static void Save() { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "results.json"), JsonUtility.ToJson(report, true)); }
    static void Check(bool ok, string name, string details = "")
    {
        report.checks.Add(new CheckResult { name = name, passed = ok, details = details }); Save();
    }
    static void Log(string condition, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            report.errors.Add(condition + "\n" + stack);
    }
    static async Task Frames(int count = 2)
    {
        int end = Time.frameCount + count; float timeout = Time.realtimeSinceStartup + 12f;
        while (Time.frameCount < end) { if (Time.realtimeSinceStartup > timeout) throw new Exception("frame timeout"); await Task.Yield(); }
    }
    static async Task Seconds(float duration)
    {
        float end = Time.time + duration, timeout = Time.realtimeSinceStartup + duration + 12f;
        while (Time.time < end) { if (Time.realtimeSinceStartup > timeout) throw new Exception("time timeout"); await Frames(1); }
    }
    static async Task Until(Func<bool> condition, float timeout, string message)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!condition()) { if (Time.realtimeSinceStartup > end) throw new Exception(message); await Frames(1); }
    }
    static async Task Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); await Frames(); }
    static async Task Tap(Key key) { await Keys(); await Keys(key); await Keys(); }
    static async Task Hold(float seconds, params Key[] keys) { await Keys(keys); await Seconds(seconds); await Keys(); }
    static async Task Stage(string name, Func<Task> action)
    {
        report.phase = name; Save();
        try { await action(); }
        catch (Exception e) { Check(false, name + " exception", e.ToString()); }
        await Keys();
    }
    static void FocusGame()
    {
        Type type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        EditorWindow.GetWindow(type).Focus();
    }
    static async Task Capture(string name)
    {
        FocusGame(); await Frames(3);
        string path = Path.Combine(output, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        await Until(() => File.Exists(path) && new FileInfo(path).Length > 1000, 8f, "screenshot not written");
        report.screenshots.Add(path); Save();
    }
    static void Bind(GameObject tank)
    {
        input = tank.GetComponent<TankInput>(); control = tank.GetComponent<TankControl>();
        crew = tank.GetComponent<TankCrew>(); view = tank.GetComponent<TankCrewCameras>();
        aim = tank.GetComponent<TankAim>(); gun = tank.GetComponent<TankGun>(); motor = tank.GetComponent<TankMotor>();
        body = tank.GetComponent<Rigidbody>(); tracks = tank.GetComponent<TankTrackAnimation>();
        recoil = tank.GetComponent<TankGunRecoil>(); ballistics = tank.GetComponent<TankBallistics>();
    }
    static async Task DriverStop()
    {
        crew.SetRole(TankCrew.Role.Driver); await Tap(Key.Space);
        await Until(() => Mathf.Abs(Vector3.Dot(body.linearVelocity, body.transform.forward)) < 0.03f
            && body.angularVelocity.magnitude < 0.01f, 4f, "tank did not stop");
    }
    static async Task RoleShortcuts()
    {
        crew.SetRole(TankCrew.Role.Gunner);
        await Tap(Key.Digit1);
        Check(crew.CurrentRole == TankCrew.Role.Driver, "top-row 1 assigns Driver");
        Check(control.LeftLever == 0f && control.RightLever == 0f, "role key 1 does not activate old lever preset");
        Check(view.CurrentStation == TankCrewCameras.CrewStation.ThirdPerson, "role assignment does not enter observation");
        await Tap(Key.Digit2); Check(crew.CurrentRole == TankCrew.Role.Gunner, "top-row 2 assigns Gunner");
        await Tap(Key.Digit1); float yaw = aim.CurrentYaw;
        await Keys(Key.Digit2, Key.RightArrow);
        Check(crew.CurrentRole == TankCrew.Role.Gunner && aim.CurrentYaw != yaw, "role changes before same-frame aiming input");
        await Keys();
        await Tap(Key.Digit3); Check(crew.CurrentRole == TankCrew.Role.Commander, "top-row 3 assigns Commander");
        await Tap(Key.F3); Check(view.CurrentStation == TankCrewCameras.CrewStation.Commander, "F3 enters Commander observation without role change");
        TankKeyboardDebug debug = tiger.GetComponent<TankKeyboardDebug>();
        string label = (string)typeof(TankKeyboardDebug).GetMethod("GetRoleLabel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(debug, null);
        Check(label.Contains("COMMANDER [3]"), "HUD displays selected role and shortcut");
        await Capture("00-commander-role-shortcut");
        await Tap(Key.Digit1);
        Check(view.CurrentStation == TankCrewCameras.CrewStation.ThirdPerson, "role shortcut exits old role observation");
        Check(control.LeftLever == 0f && control.RightLever == 0f, "role shortcut leaves lever setting unchanged");
        await Tap(Key.F1); await Tap(Key.F2);
        Check(crew.CurrentRole == TankCrew.Role.Driver && view.CurrentStation == TankCrewCameras.CrewStation.Driver, "F keys do not select another role");
        await Keys(Key.Digit2, Key.F2);
        Check(crew.CurrentRole == TankCrew.Role.Gunner && view.CurrentStation == TankCrewCameras.CrewStation.Gunner, "2 plus F2 assigns role then opens observation");
        await Keys(); await Keys(Key.Digit3, Key.F3);
        Check(crew.CurrentRole == TankCrew.Role.Commander && view.CurrentStation == TankCrewCameras.CrewStation.Commander, "3 plus F3 assigns role then opens observation");
        await Keys(); crew.enabled = false; await Tap(Key.Digit1); crew.enabled = true;
        Check(crew.CurrentRole == TankCrew.Role.Commander, "disabled crew rejects test role assignment");
        await Tap(Key.Digit1); await Tap(Key.Space);
    }
    static async Task Movement()
    {
        await DriverStop(); await Capture("01-third-person");
        Vector3 start = body.position; float began = Time.time;
        await Hold(0.25f, Key.W, Key.I); float elapsed = Time.time - began;
        Check(control.LeftLever > 0.2f && control.LeftLever < elapsed * 1.3f + .03f, "W/I increment once per frame", "lever=" + control.LeftLever + ", elapsed=" + elapsed);
        Check(Vector3.Distance(start, body.position) > 0.01f && motor.ForwardCommand > 0f, "keyboard produces actual forward movement");
        float held = control.LeftLever; await Seconds(.15f);
        Check(control.LeftLever == held, "release holds lever setting");
        await Tap(Key.Q); Check(control.LeftLever == 0f && control.RightLever > 0f, "Q neutralizes left only");
        await Tap(Key.U); Check(control.RightLever == 0f, "U neutralizes right only");
        await DriverStop(); await Hold(.25f, Key.S, Key.K);
        Check(control.LeftLever < 0f && control.RightLever < 0f, "S/K decrease both levers");
        await DriverStop();
        Vector2[] presets = { new Vector2(1,1), new Vector2(1,.3f), new Vector2(1,-1), new Vector2(-1,1), new Vector2(-1,-1), new Vector2(-1,-.3f) };
        Key[] keys = { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6 };
        for (int i = 0; i < 6; i++)
        {
            await Tap(keys[i]); await Seconds(.08f);
            Check(control.LeftLever == presets[i].x && control.RightLever == presets[i].y, "preset " + (i + 1));
            Check(Mathf.Abs(motor.ForwardCommand - (presets[i].x + presets[i].y) * .5f) < .001f
                && Mathf.Abs(motor.TurnCommand - (presets[i].x - presets[i].y) * .5f) < .001f, "preset physics command " + (i + 1));
        }
        await DriverStop(); await Tap(Key.Numpad3); float yaw = body.rotation.eulerAngles.y;
        await Seconds(.5f);
        Check(Mathf.DeltaAngle(yaw, body.rotation.eulerAngles.y) > 2f, "actual clockwise pivot");
        Check(tracks.LeftTrackSpeed > 0f && tracks.RightTrackSpeed < 0f, "track animation follows opposite wheel speeds");
        await DriverStop(); await Tap(Key.Numpad4); yaw = body.rotation.eulerAngles.y;
        await Seconds(.5f); Check(Mathf.DeltaAngle(yaw, body.rotation.eulerAngles.y) < -2f, "actual counterclockwise pivot");
        await DriverStop(); start = body.position; Vector3 forward = body.transform.forward;
        await Tap(Key.Numpad5); await Seconds(.6f);
        Check(Vector3.Dot(body.position - start, forward) < -.05f, "actual reverse displacement");
        await DriverStop(); float leftPhase = tracks.LeftPhase, rightPhase = tracks.RightPhase;
        await Seconds(.15f);
        Check(Mathf.Abs(tracks.LeftPhase - leftPhase) < .005f && Mathf.Abs(tracks.RightPhase - rightPhase) < .005f, "tracks hold stopped pose");
        await Hold(.1f, Key.W, Key.S, Key.I, Key.K);
        Check(control.LeftLever == 0f && control.RightLever == 0f, "opposite lever keys cancel");
        await Hold(1.1f, Key.W, Key.I);
        Check(control.LeftLever == 1f && control.RightLever == 1f, "lever range clamps at one");
        await Keys(Key.Numpad1, Key.Q); Check(control.LeftLever == 0f && control.RightLever == 1f, "neutral overrides preset per side");
        await DriverStop();
        TankKeyboardDebug debug = tiger.GetComponent<TankKeyboardDebug>(); debug.enabled = false;
        await Hold(.15f, Key.W, Key.I); Check(control.LeftLever > 0f, "controls work with TankKeyboardDebug disabled");
        debug.enabled = true; await DriverStop();
        body.position = origin; body.rotation = rotation; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
        await Seconds(.15f);
    }
    static async Task Collision()
    {
        await DriverStop();
        GameObject wall = null;
        foreach (GameObject root in testScene.GetRootGameObjects()) if (root.name.StartsWith("Front Test Wall")) wall = root;
        if (wall == null) throw new Exception("saved test wall missing");
        Vector3 wallPosition = wall.transform.position, wallScale = wall.transform.localScale;
        Quaternion wallRotation = wall.transform.rotation;
        BoxCollider hull = tiger.GetComponent<BoxCollider>();
        try
        {
            body.position = origin; body.rotation = rotation; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            body.transform.SetPositionAndRotation(origin, rotation);
            Physics.SyncTransforms(); await Frames(3);
            Vector3 forwardAxis = rotation * Vector3.forward;
            float frontExtent = 0f;
            List<Collider> owners = new List<Collider>();
            foreach (Collider c in tiger.GetComponentsInChildren<Collider>(true))
            {
                if (!c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger || c.attachedRigidbody != body) continue;
                owners.Add(c);
                Vector3 extents = c.bounds.extents;
                float extent = Vector3.Dot(c.bounds.center - body.position, forwardAxis)
                    + Mathf.Abs(forwardAxis.x) * extents.x + Mathf.Abs(forwardAxis.y) * extents.y + Mathf.Abs(forwardAxis.z) * extents.z;
                frontExtent = Mathf.Max(frontExtent, extent);
            }
            wall.transform.rotation = rotation;
            wall.transform.localScale = new Vector3(8f, 6f, .15f);
            wall.transform.position = body.position + Vector3.up * hull.center.y + forwardAxis * (frontExtent + 1.2f);
            Physics.SyncTransforms(); await Frames();
            Collider fixtureWall = wall.GetComponent<Collider>();
            bool fixtureOverlap = false;
            foreach (Collider c in owners)
                fixtureOverlap |= Physics.ComputePenetration(c, c.transform.position, c.transform.rotation,
                    fixtureWall, fixtureWall.transform.position, fixtureWall.transform.rotation, out Vector3 fixtureDirection, out float fixtureDepth);
            Check(!fixtureOverlap, "wall fixture starts separated from every owner collider", "front extent=" + frontExtent + ", colliders=" + owners.Count);
            Vector3 start = body.position, forward = body.transform.forward;
            await Tap(Key.Numpad1); await Seconds(1.8f);
            float travelled = Vector3.Dot(body.position - start, forward);
            Check(travelled > .2f && travelled < 1.4f, "actual hull stops at wall", "travelled=" + travelled);
            Check(motor.ForwardCommand == 1f && Mathf.Abs(Vector3.Dot(body.linearVelocity, forward)) < .12f, "wall blocks motion while forward command remains active");
            Collider wallCollider = wall.GetComponent<Collider>();
            bool overlap = Physics.ComputePenetration(hull, hull.transform.position, hull.transform.rotation,
                wallCollider, wallCollider.transform.position, wallCollider.transform.rotation, out Vector3 direction, out float depth);
            Check(!overlap || depth < .025f, "wall collision avoids significant penetration", "depth=" + depth);
            Check(Mathf.Abs(tracks.LeftTrackSpeed) < .15f && Mathf.Abs(tracks.RightTrackSpeed) < .15f, "blocked tank tracks follow actual stopped hull");
            await Capture("01b-wall-collision");
        }
        finally
        {
            await Tap(Key.Space);
            wall.transform.SetPositionAndRotation(wallPosition, wallRotation); wall.transform.localScale = wallScale;
            body.position = origin; body.rotation = rotation; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms(); await Frames();
        }
    }
    static async Task AimAndBlock()
    {
        crew.SetRole(TankCrew.Role.Gunner); await Frames();
        await Hold(.15f, Key.W, Key.I, Key.Numpad1);
        Check(control.LeftLever == 0f && control.RightLever == 0f, "gunner movement keys are rejected");
        float yaw = aim.CurrentYaw, begin = Time.time;
        await Hold(.2f, Key.RightArrow); float elapsed = Time.time - begin;
        float change = Mathf.DeltaAngle(yaw, aim.CurrentYaw);
        Check(change > 0f && change < aim.YawSpeed * elapsed * 1.3f + 1f, "right arrow rotates once per frame", "yaw change=" + change);
        yaw = aim.CurrentYaw; await Hold(.2f, Key.LeftArrow);
        Check(Mathf.DeltaAngle(yaw, aim.CurrentYaw) < 0f, "left arrow reverses turret");
        await Hold(1.2f, Key.UpArrow); Check(Mathf.Abs(aim.CurrentPitch - aim.MaxPitch) < .01f, "upper gun angle limit");
        await Hold(1.6f, Key.DownArrow); Check(Mathf.Abs(aim.CurrentPitch - aim.MinPitch) < .01f, "lower gun angle limit");
        yaw = aim.CurrentYaw; float pitch = aim.CurrentPitch;
        await Hold(.1f, Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow);
        Check(Mathf.Abs(Mathf.DeltaAngle(yaw, aim.CurrentYaw)) < .01f && Mathf.Abs(pitch - aim.CurrentPitch) < .01f, "opposite aim keys cancel");
        while (aim.CurrentPitch < 1f) { await Keys(Key.UpArrow); }
        await Keys();
        int shots = gun.ShotCount; await Tap(Key.Enter);
        Check(gun.ShotCount == shots && gun.State == TankGun.ChamberState.EmptyOpen, "unloaded Enter is rejected");
        Check(recoil.RecoilPhase == 1f, "rejected shot has no recoil");
        await Tap(Key.T); Check(gun.State == TankGun.ChamberState.EmptyClosed, "T closes empty chamber");
        await Tap(Key.Y); Check(gun.State == TankGun.ChamberState.EmptyOpen, "Y opens empty chamber");
        crew.SetRole(TankCrew.Role.Driver); await Frames();
        string last = gun.LastWeaponRequest; yaw = aim.CurrentYaw;
        await Hold(.1f, Key.RightArrow, Key.R, Key.T, Key.Y, Key.Enter);
        Check(gun.LastWeaponRequest == last && gun.State == TankGun.ChamberState.EmptyOpen, "driver weapon requests never reach gun");
        Check(aim.CurrentYaw == yaw, "driver cannot aim");
    }
    static async Task LoadAndFire()
    {
        crew.SetRole(TankCrew.Role.Gunner);
        Vector3 center = gun.Muzzle.position + gun.Muzzle.forward * 8f + .5f * Physics.gravity * Mathf.Pow(8f / gun.ProjectileSpeed, 2f);
        Collider targetCollider = target.GetComponentInChildren<Collider>();
        target.transform.position += center - targetCollider.bounds.center;
        Physics.SyncTransforms(); await Seconds(.15f);
        Check(ballistics.Current.HasHit && ballistics.Current.Collider.GetComponentInParent<TankHealth>() == target,
            "ballistics predicts actual target", ballistics.Current.State + " / " + (ballistics.Current.Collider != null ? ballistics.Current.Collider.name : ballistics.Current.Reason));
        Check(ballistics.MarkerVisible, "predicted impact marker is visible");
        for (int i = 0; i < 4; i++)
        {
            crew.SetRole(i == 0 ? TankCrew.Role.Commander : TankCrew.Role.Gunner);
            await Tap(Key.R); Check(gun.State == TankGun.ChamberState.LoadedOpen, "keyboard insert shot " + (i + 1));
            await Tap(Key.T); Check(gun.State == TankGun.ChamberState.Ready, "keyboard close shot " + (i + 1));
            if (i == 0)
            {
                await Tap(Key.Enter); Check(gun.ShotCount == 0 && gun.State == TankGun.ChamberState.Ready, "commander cannot fire loaded gun");
                crew.SetRole(TankCrew.Role.Gunner);
            }
            int count = gun.ShotCount; await Keys(Key.Enter);
            Check(gun.ShotCount == count + 1 && gun.State == TankGun.ChamberState.Recovering, "Enter commits one shot " + (i + 1));
            Check(gun.LastProjectile != null, "independent projectile spawned " + (i + 1));
            Check(recoil.IsRecoiling, "successful shot starts recoil " + (i + 1));
            if (i == 0) await Capture("02-shot-and-ballistics");
            await Until(() => target.CurrentHP <= 75f - i * 25f, 3f, "projectile failed to damage target");
            Check(Mathf.Abs(target.CurrentHP - (75f - i * 25f)) < .01f, "actual hit HP " + (75 - i * 25));
            Check(gun.LastImpactComparison.Comparable && gun.LastImpactComparison.Passed, "predicted and actual impact agree " + (i + 1), "error=" + gun.LastImpactComparison.Error);
            await Until(() => gun.State == TankGun.ChamberState.EmptyOpen, 3f, "recovery did not finish");
            Check(gun.ShotCount == count + 1, "held Enter does not refire " + (i + 1));
            await Keys();
        }
        Check(target.Destroyed && target.DestructionCount == 1 && target.DamageHitCount == 4, "four real hits destroy target once");
        await Capture("03-target-destroyed");
        await Tap(Key.R); await Tap(Key.T); await Tap(Key.Enter);
        input.enabled = false;
        await Until(() => gun.State == TankGun.ChamberState.EmptyOpen, 3f, "disabled input stopped recovery");
        Check(gun.State == TankGun.ChamberState.EmptyOpen, "recovery advances with TankInput disabled");
        input.enabled = true;
        Check(target.DestructionCount == 1 && target.DamageHitCount == 4, "destroyed target rejects extra damage");
    }
    static void CameraCount(string name)
    {
        int cams = 0, audio = 0;
        foreach (Camera c in Object.FindObjectsByType<Camera>()) if (c.enabled && c.gameObject.activeInHierarchy) cams++;
        foreach (AudioListener a in Object.FindObjectsByType<AudioListener>()) if (a.enabled && a.gameObject.activeInHierarchy) audio++;
        Check(cams == 1 && audio == 1, name, "cameras=" + cams + ", listeners=" + audio);
    }
    static async Task MovingCombat()
    {
        await DriverStop();
        target.gameObject.SetActive(false);
        GameObject movingTarget = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TankPrototype/Prefabs/TankTarget.prefab"));
        TankHealth movingHealth = movingTarget.GetComponent<TankHealth>();
        try
        {
            await Tap(Key.Numpad2); await Seconds(.35f);
            Check(body.linearVelocity.magnitude > .15f && body.angularVelocity.magnitude > .02f, "tank actually moves and turns before crew handoff");
            crew.SetRole(TankCrew.Role.Gunner);
            float oldYaw = aim.CurrentYaw;
            await Hold(.15f, Key.RightArrow, Key.UpArrow);
            Check(aim.CurrentYaw != oldYaw && control.LeftLever == 1f && control.RightLever == .3f, "gunner aims while driver lever settings keep movement");
            await Tap(Key.R); await Tap(Key.T);
            Vector3 point = gun.Muzzle.position + gun.Muzzle.forward * 8f + .5f * Physics.gravity * Mathf.Pow(8f / gun.ProjectileSpeed, 2f);
            movingTarget.transform.position += point - movingTarget.GetComponentInChildren<Collider>().bounds.center;
            Physics.SyncTransforms();
            int count = gun.ShotCount;
            await Keys(Key.RightArrow, Key.Enter);
            Check(gun.ShotCount == count + 1 && body.linearVelocity.magnitude > .15f, "keyboard shot succeeds during moving aim");
            await Keys();
            await Until(() => movingHealth.CurrentHP < 100f, 3f, "moving shot failed to hit target");
            Check(movingHealth.CurrentHP == 75f, "moving and turning shot applies real damage");
            await Capture("03b-moving-fire");
            await Until(() => gun.State == TankGun.ChamberState.EmptyOpen, 3f, "moving shot recovery failed");
        }
        finally { Object.Destroy(movingTarget); await DriverStop(); }
    }
    static async Task Observation()
    {
        for (int role = 1; role <= 3; role++)
        {
            crew.SetRole((TankCrew.Role)role); await Frames();
            for (int station = 0; station < 3; station++)
            {
                await Tap(Key.F4); await Tap(station == 0 ? Key.F1 : station == 1 ? Key.F2 : Key.F3);
                Check((int)view.CurrentStation == (role == station + 1 ? station : 3), "observation role " + role + " F" + (station + 1));
                Check((int)crew.CurrentRole == role, "F key preserves role " + role + "/" + station);
                CameraCount("single active camera/audio " + role + "/" + station);
            }
            await Tap(role == 1 ? Key.F1 : role == 2 ? Key.F2 : Key.F3);
            Check(view.ActiveCamera != null && view.ActiveCamera.CompareTag("MainCamera"), "active view MainCamera tag role " + role);
            await Capture(role == 1 ? "04-driver-view" : role == 2 ? "05-gunner-view" : "06-commander-view");
        }
        FocusGame(); await Frames(); report.focused = Application.isFocused;
        Check(Application.isFocused, "Game view focused for real mouse route");
        float yaw = view.CommanderYaw;
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(100f, -20f) }); await Frames();
        Check(view.CommanderYaw == yaw, "mouse without RMB does not rotate");
        float pitch = view.CommanderPitch;
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(100f, -20f) }.WithButton(MouseButton.Right)); await Frames();
        Check(Mathf.Abs(Mathf.DeltaAngle(yaw, view.CommanderYaw) - 15f) < .1f && Mathf.Abs(view.CommanderPitch - pitch - 3f) < .1f,
            "RMB mouse routes through TankInput exactly once", "yaw delta=" + Mathf.DeltaAngle(yaw, view.CommanderYaw));
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(0f, 100000f) }.WithButton(MouseButton.Right)); await Frames();
        Check(view.CommanderPitch == -80f, "observation lower pitch clamp");
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(0f, -100000f) }.WithButton(MouseButton.Right)); await Frames();
        Check(view.CommanderPitch == 80f, "observation upper pitch clamp");
        InputSystem.QueueStateEvent(mouse, new MouseState()); await Frames();
        crew.SetRole(TankCrew.Role.Gunner); await Frames();
        Check(view.CurrentStation == TankCrewCameras.CrewStation.ThirdPerson, "role change exits previous observation");
        yaw = view.CommanderYaw;
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(100f, 0f) }.WithButton(MouseButton.Right)); await Frames();
        Check(view.CommanderYaw == yaw, "gunner mouse cannot rotate commander view");
        InputSystem.QueueStateEvent(mouse, new MouseState());
        crew.SetRole(TankCrew.Role.None); await Hold(.1f, Key.W, Key.I, Key.R, Key.Enter, Key.RightArrow, Key.F3);
        Check(control.LeftLever == 0f && control.RightLever == 0f && view.CurrentStation == TankCrewCameras.CrewStation.ThirdPerson, "unassigned role cannot drive or observe");
        crew.SetRole(TankCrew.Role.Commander); await Tap(Key.F3); crew.enabled = false; await Frames();
        Check(view.CurrentStation == TankCrewCameras.CrewStation.ThirdPerson, "disabled crew exits observation");
        crew.enabled = true;
    }
    static async Task BasicFallback()
    {
        tiger.SetActive(false); basic.SetActive(true); Bind(basic); await Frames(4);
        Check(crew.CurrentRole == TankCrew.Role.Driver && view != null, "saved basic tank has crew and scene camera binding");
        await Tap(Key.F1); Check(view.CurrentStation == TankCrewCameras.CrewStation.Driver, "basic F1 driver observation"); CameraCount("basic driver camera/audio");
        crew.SetRole(TankCrew.Role.Gunner); await Frames();
        Check(view.CurrentStation == TankCrewCameras.CrewStation.ThirdPerson && view.ActiveCamera != null && view.ActiveCamera.enabled,
            "basic external camera restored after role change"); CameraCount("basic external overview camera/audio");
        await Capture("07-basic-external-overview");
        await Tap(Key.F2); Check(view.CurrentStation == TankCrewCameras.CrewStation.Gunner, "basic F2 re-enters gunner observation"); CameraCount("basic gunner camera/audio");
        crew.SetRole(TankCrew.Role.Commander); await Frames(); await Tap(Key.F3);
        Check(view.CurrentStation == TankCrewCameras.CrewStation.Commander, "basic F3 commander observation");
        crew.enabled = false; await Frames(); Check(view.ActiveCamera != null && view.ActiveCamera.enabled, "basic disabled crew returns to external overview");
        crew.enabled = true; crew.SetRole(TankCrew.Role.Driver); await Tap(Key.F1);
        view.enabled = false; await Frames();
        Check(view.ActiveCamera != null && !view.ActiveCamera.enabled, "camera manager disable restores originally disabled scene overview");
        view.enabled = true; await Frames();
        FieldInfo crewField = typeof(TankControl).GetField("crew", BindingFlags.Instance | BindingFlags.NonPublic);
        crewField.SetValue(control, null); float held = control.LeftLever; control.SetLeverInputs(.9f, .9f);
        Check(control.LeftLever == held && !control.RequestInsert() && !control.RequestFire(), "missing crew fails closed");
        crewField.SetValue(control, crew);
        FieldInfo cameraField = typeof(TankControl).GetField("cameras", BindingFlags.Instance | BindingFlags.NonPublic);
        cameraField.SetValue(control, null); await Tap(Key.F1); await Tap(Key.F4); await Hold(.15f, Key.W, Key.I);
        Check(control.LeftLever > 0f && control.RightLever > 0f, "missing optional camera leaves movement working");
        cameraField.SetValue(control, view); await DriverStop();
    }

    [MenuItem("Tools/Tank Verification/Run Full Play Checks")]
    public static async void Run()
    {
        if (running || !EditorApplication.isPlaying) return;
        running = true;
        report = new Report { unityVersion = Application.unityVersion, scene = "Assets/TankPrototype/Scenes/TankTest.unity",
            inputPath = "virtual keyboard/mouse events -> real TankInput.Update/LateUpdate -> TankControl -> TankCrew -> components" };
        Application.logMessageReceived += Log;
        List<GameObject> originalRoots = new List<GameObject>(); List<bool> originalStates = new List<bool>();
        Scene originalScene = SceneManager.GetActiveScene(); float oldScale = Time.timeScale;
        try
        {
            previousKeyboard = Keyboard.current; previousMouse = Mouse.current;
            keyboard = InputSystem.AddDevice<Keyboard>("TankFullPlayKeyboard"); keyboard.MakeCurrent();
            mouse = InputSystem.AddDevice<Mouse>("TankFullPlayMouse"); mouse.MakeCurrent(); Time.timeScale = 1f;
            HashSet<Scene> scenes = new HashSet<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i); scenes.Add(s);
                foreach (GameObject root in s.GetRootGameObjects()) { originalRoots.Add(root); originalStates.Add(root.activeSelf); root.SetActive(false); }
            }
            report.phase = "load saved TankTest additively"; Save();
            AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(report.scene, new LoadSceneParameters(LoadSceneMode.Additive));
            await Until(() => load.isDone, 12f, "test scene load failed");
            for (int i = 0; i < SceneManager.sceneCount; i++) if (!scenes.Contains(SceneManager.GetSceneAt(i))) testScene = SceneManager.GetSceneAt(i);
            SceneManager.SetActiveScene(testScene);
            foreach (GameObject root in testScene.GetRootGameObjects())
            {
                foreach (TankInput candidate in root.GetComponentsInChildren<TankInput>(true))
                    if (candidate.GetComponent<TankTrackAnimation>() != null) tiger = candidate.gameObject; else basic = candidate.gameObject;
                TankHealth health = root.GetComponentInChildren<TankHealth>(true); if (health != null) target = health;
            }
            Bind(tiger); FocusGame(); await Seconds(.5f); origin = body.position; rotation = body.rotation;
            Check(tiger.activeInHierarchy && !basic.activeInHierarchy && target != null, "saved scene initial tank and target composition");
            await Stage("top-row role assignment and observation separation", RoleShortcuts);
            await Stage("movement and track animation", Movement);
            await Stage("actual hull and wall collision", Collision);
            await Stage("aim, limits and forbidden controls", AimAndBlock);
            await Stage("loading, recoil, ballistics, actual hits and HP", LoadAndFire);
            await Stage("movement, turning, aiming and actual firing together", MovingCombat);
            await Stage("role observation and real mouse input", Observation);
            await Stage("saved basic tank external camera fallback", BasicFallback);
        }
        catch (Exception e) { Check(false, "runner exception", e.ToString()); }
        finally
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            if (testScene.IsValid() && testScene.isLoaded)
            {
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(testScene);
                if (unload != null) while (!unload.isDone) await Task.Yield();
            }
            for (int i = 0; i < originalRoots.Count; i++) if (originalRoots[i] != null) originalRoots[i].SetActive(originalStates[i]);
            Time.timeScale = oldScale;
            Application.logMessageReceived -= Log;
            bool ok = report.errors.Count == 0;
            foreach (CheckResult result in report.checks) if (!result.passed) ok = false;
            report.status = ok ? "passed" : "failed"; report.phase = "finished"; Save(); running = false;
        }
    }
}

