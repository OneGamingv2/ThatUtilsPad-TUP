using System;
using System.Collections;
using System.Collections.Generic;
using CameraMod.Button;
using CameraMod.Button.Buttons;
using CameraMod.Camera.AppearanceFeatures;
using CameraMod.Camera.Comps;
using CameraMod.Camera.Networking;
using CameraMod.Camera.Pages;
using CameraMod.Camera.Patches;
using GorillaLocomotion;
using Unity.Cinemachine;
using UnityEngine;

#pragma warning disable CS0618
namespace CameraMod.Camera
{
    public enum UpdateMode
    {
        Patch,
        Update,
        LateUpdate
    }

    public enum CameraMode
    {
        ThirdPerson,
        FirstPersonView,
        FollowPlayer,
        FreeCam,
        None
    }

    public class CameraController : MonoBehaviour
    {
        public enum TpvModes
        {
            Back,
            Front
        }

        public static CameraController Instance;
        public static UpdateMode UpdateMode = UpdateMode.Patch;
        public static bool BindEnabled = true;

        public Transform cameraTabletT;
        public Transform cameraFollowerT;
        public Transform tpvBodyFollowerT;
        public Transform thirdPersonCameraT;
        public Transform tabletCameraT;
        public Transform lensRigT;

        /// <summary>Lens that POV / FreeCam / 3RD move. Tablet GUI stays on cameraTabletT.</summary>
        public Transform LensT => lensRigT != null ? lensRigT : tabletCameraT;

        public MainPage mainPage;
        public MiscPage miscPage;
        public PovPage povPage;
        public ControlsPage controlsPage;

        public GameObject colorScreenGo;
        private readonly List<BaseButton> buttons = new List<BaseButton>();
        public List<Material> screenMats = new List<Material>();
        public List<MeshRenderer> meshRenderers = new List<MeshRenderer>();

        public UnityEngine.Camera tabletCamera;
        public UnityEngine.Camera thirdPersonCamera;

        public bool followheadrot = true;
        public bool isFaceCamera;
        public CameraMode cameraMode = CameraMode.None;

        public float minDist = 2f;
        public float fpspeed = 0.01f;
        public float smoothing = 0.07f;
        public float povStability = 1f;
        public TpvModes tpvMode = TpvModes.Back;

        private bool init;
        private bool initInProgress;
        public bool IsReady => init;
        private Vector3 velocity = Vector3.zero;
        private Vector3 filteredHeadPos;
        private Quaternion filteredHeadRot = Quaternion.identity;
        private Vector3 filteredBodyPos;
        private Quaternion filteredBodyRot = Quaternion.identity;
        private Quaternion filteredLookRot = Quaternion.identity;
        private bool poseFilterReady;
        private CameraMode poseFilterMode = CameraMode.None;

        // Anti-micro-head movement (meters / degrees). Kills tiny VR tracking jitter.
        private const float HeadPosDeadzone = 0.006f;
        private const float HeadAngDeadzoneDeg = 1.75f;
        private const float HeadFilterHz = 7.5f;
        private const float LookFilterHz = 5.5f;
        private const float TpvSmoothTime = 0.16f;
        private const float MaxSmoothSpeed = 4.5f;
        private Appearance appearance;
        private string skinPrefKey = "PokruksSkin";
        private float lastPageChangedTime;
        private float lastAnyButtonTime;
        private readonly float pageChangeButtonsTimeout = 0.22f;
        public HeadCosmeticsHider HeadCosmeticsHider;
        public bool isCosmeticsHiderInited;

        public float ButtonCooldownSeconds = 0.28f;
        public bool KeepModeOnPopOut = true;
        public bool KeepGuiInActiveModes = true;
        public bool SmoothingEnabled = true;
        public CameraMode LastModeBeforePopOut = CameraMode.None;

        public bool ButtonsTimeouted =>
            Time.time - lastPageChangedTime < pageChangeButtonsTimeout
            || Time.time - lastAnyButtonTime < ButtonCooldownSeconds;

        public void NotifyButtonPressed()
        {
            lastAnyButtonTime = Time.time;
        }

        /// <summary>
        /// Grip to freehand the tablet (LIV style). Remembers prior mode/settings and
        /// restores that mode on release when KeepModeOnPopOut is enabled.
        /// </summary>
        public void BeginHandHold()
        {
            if (cameraMode == CameraMode.FirstPersonView)
                return;

            if (cameraMode == CameraMode.FollowPlayer || cameraMode == CameraMode.ThirdPerson)
                LastModeBeforePopOut = cameraMode;

            // Freehold: float/hand cam keeps FOV/smooth/near already applied to cameras.
            cameraMode = CameraMode.None;
            ResetPoseFilters();
            ApplyPreviewIdleVisual();
        }

        public void EndHandHold()
        {
            if (!KeepModeOnPopOut)
                return;

            if (LastModeBeforePopOut == CameraMode.FollowPlayer ||
                LastModeBeforePopOut == CameraMode.ThirdPerson)
            {
                cameraMode = LastModeBeforePopOut;
                ResetPoseFilters();
                AlignLensCameras();
                ApplyPreviewIdleVisual();
            }
        }

        public static float maxAngle = 30f;
        public static float MaxAngle
        {
            get => maxAngle;
            set
            {
                maxAngle = value;
                PlayerPrefs.SetFloat("AngleClampingValue", maxAngle);
                UI.clampAngleString = value.ToString();
            }
        }

        private static bool angleClamping = true;
        public static bool AngleClamping
        {
            get => angleClamping;
            set
            {
                angleClamping = value;
                PlayerPrefs.SetInt("AngleClamping", value ? 1 : 0);
            }
        }

        public static Vector3 FirstPersonOffset = new Vector3(
            PlayerPrefs.GetFloat("FPOffset_x", 0f),
            PlayerPrefs.GetFloat("FPOffset_y", 0f),
            PlayerPrefs.GetFloat("FPOffset_z", 0f));

        public static bool RollLock = true;

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            if (UpdateMode == UpdateMode.Update)
                AnUpdate();
        }

        private void LateUpdate()
        {
            if (UpdateMode == UpdateMode.LateUpdate)
                AnUpdate();
        }

        public void SetNearClip(float val)
        {
            if (tabletCamera == null)
                return;

            // Proper clamp — the old wrap (tiny→1, big→0.01) put the cam inside walls
            // and made wood fill the screen like "super zoom".
            float near = Mathf.Clamp(val, 0.01f, 0.35f);

            tabletCamera.nearClipPlane = near;
            if (thirdPersonCamera != null && IsLivePreviewMode())
                thirdPersonCamera.nearClipPlane = near;
            if (mainPage?.NearClipText != null)
                mainPage.NearClipText.text = near.ToString("0.##");
            if (povPage?.NearText != null)
                povPage.NearText.text = near.ToString("0.##");
        }

        public void ChangeNearClip(float diff)
        {
            if (tabletCamera == null)
                return;
            SetNearClip(tabletCamera.nearClipPlane + diff);
            PlayerPrefs.SetFloat("CameraNearClip", tabletCamera.nearClipPlane);
        }

        private const float MIN_SMOOTHING = 0.001f;
        private const float MAX_SMOOTHING = 1f;

        public void SetSmoothing(float val)
        {
            smoothing = Mathf.Clamp(val, MIN_SMOOTHING, MAX_SMOOTHING);
            string display = SmoothingEnabled ? smoothing.ToString(smoothing < 0.01f ? "0.000" : "0.##") : "OFF";
            if (mainPage?.SmoothText != null)
                mainPage.SmoothText.text = display;
            if (povPage?.SmoothText != null)
                povPage.SmoothText.text = display;
        }

        public void ChangeSmoothing(float change)
        {
            // Finer steps in the ultra-low range so "super low" is controllable.
            float step = change;
            if (smoothing <= 0.05f)
                step = Mathf.Sign(change) * Mathf.Max(0.001f, Mathf.Abs(change) * 0.25f);
            else if (smoothing <= 0.15f)
                step = Mathf.Sign(change) * Mathf.Max(0.005f, Mathf.Abs(change) * 0.5f);

            SetSmoothing(smoothing + step);
            PlayerPrefs.SetFloat("CameraSmoothing", smoothing);
            RefreshControlsStatusTexts();
        }

        public void SetSmoothingEnabled(bool enabled)
        {
            SmoothingEnabled = enabled;
            PlayerPrefs.SetInt("TUP_CameraNoSmooth", enabled ? 0 : 1);
            SetSmoothing(smoothing);
            SyncAllToggleAppearances();
            RefreshControlsStatusTexts();
        }

        public void ChangeFov(float difference)
        {
            if (tabletCamera == null)
                return;
            float newFov = Mathf.Clamp(tabletCamera.fieldOfView + difference, 20f, 130f);
            SetFov(newFov);
            PlayerPrefs.SetInt("CameraFov", (int)newFov);
        }

        public void SetFov(float fov)
        {
            float newFov = Mathf.Clamp(fov, 20f, 130f);
            if (tabletCamera != null)
                tabletCamera.fieldOfView = newFov;
            // Only push FOV onto the PC/shoulder cam while TUP is driving it —
            // otherwise we fight GT's Cinemachine/third-person framing.
            if (thirdPersonCamera != null && IsLivePreviewMode())
                thirdPersonCamera.fieldOfView = newFov;
            if (mainPage?.FOVText != null)
                mainPage.FOVText.text = newFov.ToString("0");
            if (povPage?.ZoomText != null)
                povPage.ZoomText.text = newFov.ToString("0");
            if (CameraTabletAssets.IsBundleTablet(cameraTabletT))
                RefreshBundleSettingReadout();
        }

        public string GetFovDisplay()
        {
            if (tabletCamera != null)
                return tabletCamera.fieldOfView.ToString("0");
            if (thirdPersonCamera != null)
                return thirdPersonCamera.fieldOfView.ToString("0");
            return "90";
        }

        public void SetPovStability(float val)
        {
            povStability = Mathf.Clamp(val, 0.35f, 2.5f);
            if (povPage?.StableText != null)
                povPage.StableText.text = povStability.ToString("#.##");
            PlayerPrefs.SetFloat("TUP_CameraPovStable", povStability);
        }

        public void ChangePovStability(float delta)
        {
            SetPovStability(povStability + delta);
        }

        public void ChangePovOffset(int axis, float delta)
        {
            Vector3 o = FirstPersonOffset;
            if (axis == 0) o.x = Mathf.Clamp(o.x + delta, -0.35f, 0.35f);
            else if (axis == 1) o.y = Mathf.Clamp(o.y + delta, -0.35f, 0.45f);
            else o.z = Mathf.Clamp(o.z + delta, -0.45f, 0.55f);
            FirstPersonOffset = o;
            PlayerPrefs.SetFloat("FPOffset_x", o.x);
            PlayerPrefs.SetFloat("FPOffset_y", o.y);
            PlayerPrefs.SetFloat("FPOffset_z", o.z);
            RefreshPovOffsetTexts();
        }

        public void ResetPovOffsets()
        {
            FirstPersonOffset = Vector3.zero;
            PlayerPrefs.SetFloat("FPOffset_x", 0f);
            PlayerPrefs.SetFloat("FPOffset_y", 0f);
            PlayerPrefs.SetFloat("FPOffset_z", 0f);
            SetPovStability(1f);
            SetSmoothing(0.07f);
            PlayerPrefs.SetFloat("CameraSmoothing", smoothing);
            SetFov(90f);
            PlayerPrefs.SetInt("CameraFov", 90);
            SetNearClip(0.05f);
            PlayerPrefs.SetFloat("CameraNearClip", 0.05f);
            RefreshPovOffsetTexts();
        }

        private void RefreshPovOffsetTexts()
        {
            if (povPage == null)
                return;
            Vector3 o = FirstPersonOffset;
            if (povPage.SideText != null)
                povPage.SideText.text = o.x.ToString("+0.00;-0.00;0.00");
            if (povPage.HeightText != null)
                povPage.HeightText.text = o.y.ToString("+0.00;-0.00;0.00");
            if (povPage.DistText != null)
                povPage.DistText.text = o.z.ToString("+0.00;-0.00;0.00");
            if (povPage.StableText != null)
                povPage.StableText.text = povStability.ToString("#.##");
            if (povPage.ZoomText != null && tabletCamera != null)
                povPage.ZoomText.text = tabletCamera.fieldOfView.ToString("0");
            if (povPage.SmoothText != null)
                povPage.SmoothText.text = SmoothingEnabled
                    ? smoothing.ToString(smoothing < 0.01f ? "0.000" : "0.##")
                    : "OFF";
            if (povPage.NearText != null && tabletCamera != null)
                povPage.NearText.text = tabletCamera.nearClipPlane.ToString("0.##");
        }

        private void RefreshControlsBindText()
        {
            if (controlsPage?.BindValueText != null)
                controlsPage.BindValueText.text = CameraMod.Binds.GetActivateBindLabel();
            RefreshControlsStatusTexts();
        }

        private void RefreshMiscPageTexts()
        {
            if (miscPage?.MinDistText != null)
                miscPage.MinDistText.text = minDist.ToString("#.##");
            if (miscPage?.SpeedText != null)
                miscPage.SpeedText.text = fpspeed.ToString("#.##");
            if (miscPage?.TpText != null)
                miscPage.TpText.text = tpvMode.ToString();
            if (miscPage?.TpRotText != null)
                miscPage.TpRotText.text = followheadrot.ToString().ToUpperInvariant();
        }

        private void SyncAllToggleAppearances()
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] is ToggleButton toggle)
                    toggle.RefreshAppearance();
            }
        }

        private void RefreshControlsStatusTexts()
        {
            if (controlsPage == null)
                return;

            if (controlsPage.KeepGuiStatusText != null)
                controlsPage.KeepGuiStatusText.text = KeepGuiInActiveModes ? "ON" : "OFF";
            if (controlsPage.SmoothStatusText != null)
                controlsPage.SmoothStatusText.text = SmoothingEnabled
                    ? smoothing.ToString(smoothing < 0.01f ? "0.000" : "#.##")
                    : "OFF (snap)";
        }

        private void ShowOnlyPage(GameObject page)
        {
            if (mainPage?.GO != null) mainPage.GO.SetActive(page == mainPage.GO);
            if (miscPage?.GO != null) miscPage.GO.SetActive(page == miscPage.GO);
            if (povPage?.GO != null) povPage.GO.SetActive(page == povPage.GO);
            if (controlsPage?.GO != null) controlsPage.GO.SetActive(page == controlsPage.GO);
            lastPageChangedTime = Time.time;

            if (page == miscPage?.GO)
                RefreshMiscPageTexts();
            if (page == povPage?.GO)
                RefreshPovOffsetTexts();
            if (page == controlsPage?.GO)
                RefreshControlsBindText();

            SyncAllToggleAppearances();
        }

        private void InitAppearance()
        {
            if (appearance?.go == null || cameraTabletT == null)
                return;

            appearance.transform.SetParent(cameraTabletT, false);
            appearance.transform.localScale = Vector3.one;
            appearance.transform.localPosition = new Vector3(0f, 0f, 0.012f);
            appearance.transform.localRotation = Quaternion.identity;
            appearance.transform.SetAsFirstSibling();
            appearance.isOwner = false;
        }

        private void SetSkin(string skinName)
        {
            // Asset GoPro tablet already has its own shell — procedural "Default" skin
            // was a near-black box covering the entire camera (looked like old GUI).
            if (CameraTabletAssets.IsBundleTablet(cameraTabletT))
            {
                if (appearance != null && appearance.go != null)
                    Destroy(appearance.go);
                appearance = null;
                return;
            }

            if (appearance != null && appearance.go != null)
                Destroy(appearance.go);
            appearance = AppearancesLoader.InstantiateAppearance(skinName);
            InitAppearance();
            PlayerPrefs.SetString(skinPrefKey, GetSkin());
        }

        public string GetSkin() => appearance != null ? appearance.name : "Default";

        public void Init()
        {
            if (init || initInProgress)
                return;
            StartCoroutine(InitWhenReady());
        }

        private IEnumerator InitWhenReady()
        {
            initInProgress = true;

            Transform shoulder = null;
            Transform follower = null;
            for (int i = 0; i < 120; i++)
            {
                shoulder = FindThirdPersonCamera();
                follower = FindCameraFollower();
                if (shoulder != null && follower != null && GorillaTagger.Instance != null)
                    break;
                yield return null;
            }

            if (shoulder == null || follower == null || GorillaTagger.Instance == null)
            {
                Debug.LogError("[TUP Camera] Init failed: player camera objects not found.");
                initInProgress = false;
                yield break;
            }

            try
            {
                InitCore(shoulder, follower);
            }
            catch (Exception ex)
            {
                Debug.LogError("[TUP Camera] InitCore exception: " + ex);
                init = false;
            }

            initInProgress = false;
        }

        private void InitCore(Transform shoulder, Transform follower)
        {
            GorillaTagger tagger = GorillaTagger.Instance;

            colorScreenGo = ProceduralGui.BuildColorScreen();

            GameObject tabletGo = CameraTabletAssets.TryInstantiateTablet();
            bool bundleTablet = tabletGo != null;
            if (bundleTablet)
            {
                cameraTabletT = tabletGo.transform;
                Debug.Log("[TUP Camera] Using tup-cameramod AssetBundle tablet.");
            }
            else
            {
                cameraTabletT = ProceduralGui.BuildCameraTablet().transform;
                Debug.Log("[TUP Camera] AssetBundle missing — using procedural tablet.");
            }

            cameraTabletT.localScale = Vector3.one;
            UnityEngine.Object.DontDestroyOnLoad(cameraTabletT.gameObject);
            UnityEngine.Object.DontDestroyOnLoad(colorScreenGo);

            SetSkin(PlayerPrefs.GetString(skinPrefKey, "Default"));

            thirdPersonCameraT = shoulder;
            // Do NOT disable Cinemachine here — that froze the PC/monitor cam.
            // Never hijack Shoulder unless DrivePcMonitor is on.
            tpvBodyFollowerT = tagger.bodyCollider != null ? tagger.bodyCollider.transform : tagger.transform;
            thirdPersonCamera = thirdPersonCameraT.GetComponent<UnityEngine.Camera>();
            CaptureGtPcCameraDefaults();
            cameraFollowerT = follower;

            if (bundleTablet)
            {
                tabletCamera = CameraTabletAssets.EnsurePreviewCamera(cameraTabletT);
                tabletCameraT = tabletCamera != null ? tabletCamera.transform : null;
            }
            else
            {
                tabletCameraT = cameraTabletT.Find("Camera");
                if (tabletCameraT == null)
                    throw new Exception("Procedural tablet missing Camera child");
                tabletCamera = tabletCameraT.GetComponent<UnityEngine.Camera>();
            }

            if (tabletCamera == null || tabletCameraT == null)
                throw new Exception("Tablet camera failed to init");

            DetachLensFromTablet();
            ConfigureTabletPreviewCamera();

            if (bundleTablet)
            {
                // Grips already wired in EnsurePreviewCamera; also keep legacy names empty.
            }
            else
            {
                Transform leftGrab = cameraTabletT.Find("LeftGrabCol");
                Transform rightGrab = cameraTabletT.Find("RightGrabCol");
                if (leftGrab != null) leftGrab.gameObject.AddComponent<LeftGrabTrigger>();
                if (rightGrab != null) rightGrab.gameObject.AddComponent<RightGrabTrigger>();
            }

            Transform mainPageT = cameraTabletT.Find("MainPage")
                                  ?? cameraTabletT.Find("Main")
                                  ?? CameraTabletAssets.FindDeep(cameraTabletT, "Main");
            Transform miscPageT = cameraTabletT.Find("MiscPage");
            Transform povPageT = cameraTabletT.Find("PovPage");
            Transform controlsPageT = cameraTabletT.Find("ControlsPage");

            // Bundle tablet has a single Main panel — stub the other pages so null checks stay safe.
            if (miscPageT == null)
            {
                miscPageT = new GameObject("MiscPage").transform;
                miscPageT.SetParent(cameraTabletT, false);
                miscPageT.gameObject.SetActive(false);
            }
            if (povPageT == null)
            {
                povPageT = new GameObject("PovPage").transform;
                povPageT.SetParent(cameraTabletT, false);
                povPageT.gameObject.SetActive(false);
            }
            if (controlsPageT == null)
            {
                controlsPageT = new GameObject("ControlsPage").transform;
                controlsPageT.SetParent(cameraTabletT, false);
                controlsPageT.gameObject.SetActive(false);
            }

            if (mainPageT == null)
                throw new Exception("Tablet missing Main / MainPage");

            mainPage = new MainPage(mainPageT);
            miscPage = new MiscPage(miscPageT);
            povPage = new PovPage(povPageT);
            controlsPage = new ControlsPage(controlsPageT);
            RefreshControlsBindText();

            if (bundleTablet)
                RegisterBundleTabletButtons();
            else
                RegisterButtons();

            // Keep GT's PC / third-person camera where the game put it.
            // Desktop (no HMD): Shoulder must stay DISABLED — it has depth 2 and would
            // draw over Main Camera, causing the zoomed monitor bug. VR: leave to GT.
            if (thirdPersonCamera != null)
            {
                thirdPersonCamera.targetTexture = null;
                if (IsDesktopNoHmd())
                    thirdPersonCamera.enabled = false;
                SetCinemachineSuppressed(false);
            }

            SyncPreviewCameraFromThirdPerson();
            if (tabletCamera != null)
                tabletCamera.enabled = false;

            SetupColorScreen();

            if (miscPage?.GO != null)
                miscPage.GO.SetActive(false);
            if (povPage?.GO != null)
                povPage.GO.SetActive(false);
            if (tabletCamera != null)
                tabletCamera.nearClipPlane = 0.1f;

            init = true;

            SetFov(Mathf.Clamp(PlayerPrefs.GetInt("CameraFov", 90), 60, 120));
            // Sanitize bad prefs from the old near-clip wrap bug
            float savedNear = PlayerPrefs.GetFloat("CameraNearClip", 0.05f);
            if (savedNear < 0.01f || savedNear > 0.35f)
                savedNear = 0.05f;
            SetNearClip(savedNear);
            angleClamping = PlayerPrefs.GetInt("AngleClamping", 1) == 1;
            maxAngle = PlayerPrefs.GetFloat("AngleClampingValue", 30f);
            KeepModeOnPopOut = PlayerPrefs.GetInt("TUP_CameraKeepMode", 1) == 1;
            KeepGuiInActiveModes = PlayerPrefs.GetInt("TUP_CameraKeepGui", 1) == 1;
            DrivePcMonitor = PlayerPrefs.GetInt("TUP_DrivePcMonitor", 0) == 1;
            SmoothingEnabled = PlayerPrefs.GetInt("TUP_CameraNoSmooth", 0) == 0;
            SetSmoothing(PlayerPrefs.GetFloat("CameraSmoothing", 0.07f));
            ButtonCooldownSeconds = PlayerPrefs.GetFloat("TUP_CameraBtnCooldown", 0.28f);
            SetPovStability(PlayerPrefs.GetFloat("TUP_CameraPovStable", 1f));
            RefreshPovOffsetTexts();
            RefreshMiscPageTexts();
            RefreshControlsBindText();
            SyncAllToggleAppearances();
            ApplyPreviewIdleVisual();

            Binds.Init();
            // Desktop: park tablet to the side so it doesn't fill the monitor.
            // Do NOT leave Shoulder looking at a tablet jammed in front of the head.
            PlaceTabletBesidePlayer();
            ForceFixZoomedPcView();
            ApplyPreviewIdleVisual();
            Debug.Log("[TUP Camera] Ready. DrivePcMonitor=" + DrivePcMonitor + " Buttons=" + buttons.Count);
        }

        private void DetachLensFromTablet()
        {
            if (tabletCameraT == null)
                return;

            if (lensRigT == null)
            {
                GameObject rig = new GameObject("TUP_CameraLens");
                UnityEngine.Object.DontDestroyOnLoad(rig);
                lensRigT = rig.transform;
            }

            // Keep current world pose (outward-facing) but free the tablet from the lens.
            Vector3 worldPos = tabletCameraT.position;
            Quaternion worldRot = tabletCameraT.rotation;
            lensRigT.SetPositionAndRotation(worldPos, worldRot);
            tabletCameraT.SetParent(lensRigT, false);
            tabletCameraT.localPosition = Vector3.zero;
            tabletCameraT.localRotation = Quaternion.identity;
            tabletCameraT.localScale = Vector3.one;
            isFaceCamera = false;
        }

        /// <summary>
        /// Place the lens at a safe outward view. Desktop FreeCam prefers the player head
        /// so the PC camera doesn't start inside stump/wood geometry (looks "super zoomed").
        /// </summary>
        public void SnapLensToTabletOutward()
        {
            if (LensT == null)
                return;

            Vector3 pos;
            Quaternion rot;

            Transform head = null;
            try
            {
                if (GorillaTagger.Instance != null)
                    head = GorillaTagger.Instance.headCollider != null
                        ? GorillaTagger.Instance.headCollider.transform
                        : GorillaTagger.Instance.mainCamera?.transform;
            }
            catch { /* ignore */ }

            bool desktop = false;
            try
            {
                desktop = !UnityEngine.XR.XRSettings.isDeviceActive;
            }
            catch { desktop = true; }

            if (head != null && (desktop || cameraTabletT == null))
            {
                // Stand behind the eyes a bit, look where the player looks
                pos = head.position + head.forward * -0.15f + head.up * 0.05f;
                rot = head.rotation;
            }
            else if (cameraTabletT != null)
            {
                pos = cameraTabletT.TransformPoint(new Vector3(0f, 0.09f, -0.08f));
                rot = cameraTabletT.rotation * Quaternion.Euler(0f, 180f, 0f);
            }
            else
                return;

            // Nudge out of solid geometry so we don't embed in bark/wood
            pos = NudgeOutOfGeometry(pos, rot * Vector3.forward);

            LensT.SetPositionAndRotation(pos, rot);
            isFaceCamera = false;
        }

        private static Vector3 NudgeOutOfGeometry(Vector3 pos, Vector3 lookDir)
        {
            try
            {
                if (Physics.CheckSphere(pos, 0.12f, ~0, QueryTriggerInteraction.Ignore))
                {
                    // Pull back along look, then up
                    Vector3 back = pos - lookDir.normalized * 0.55f + Vector3.up * 0.25f;
                    if (!Physics.CheckSphere(back, 0.12f, ~0, QueryTriggerInteraction.Ignore))
                        return back;
                    return pos + Vector3.up * 0.8f - lookDir.normalized * 0.35f;
                }
            }
            catch { /* physics may be unavailable early */ }
            return pos;
        }

        /// <summary>Restore sane FOV / near so PC view is never telephoto-or-inside-mesh.</summary>
        public void ResetViewFraming()
        {
            SetFov(90f);
            PlayerPrefs.SetInt("CameraFov", 90);
            SetNearClip(0.05f);
            PlayerPrefs.SetFloat("CameraNearClip", 0.05f);
            RefreshPovOffsetTexts();
        }

        public void EnableFreeCam()
        {
            if (!init)
                return;
            if (UI.Instance != null)
                UI.Instance.freecam = true;
            cameraMode = CameraMode.FreeCam;
            // FreeCam always owns the PC monitor — not just the tablet preview screen.
            DrivePcMonitor = true;
            ResetViewFraming();
            SnapLensToTabletOutward();
            ResetPoseFilters();
            AlignLensCameras();
            ApplyPreviewIdleVisual();
            EnsureTabletGuiVisible();
            SyncPcCameraFromLens();
        }

        public void EnsureTabletGuiVisible()
        {
            if (cameraTabletT == null)
                return;

            // Tablet GUI never rides the lens — keep it available while modes run.
            if (KeepGuiInActiveModes || cameraMode == CameraMode.None || cameraMode == CameraMode.FreeCam)
            {
                SetTabletVisibility(true);
                if (mainPage?.GO != null)
                    mainPage.GO.SetActive(true);
            }
        }

        public bool IsLivePreviewMode()
        {
            return cameraMode == CameraMode.FirstPersonView
                   || cameraMode == CameraMode.ThirdPerson
                   || cameraMode == CameraMode.FollowPlayer
                   || cameraMode == CameraMode.FreeCam;
        }

        private void ConfigureTabletPreviewCamera()
        {
            if (tabletCamera == null)
                return;

            tabletCamera.stereoTargetEye = StereoTargetEyeMask.None;
            tabletCamera.allowHDR = false;
            tabletCamera.allowMSAA = false;
            tabletCamera.depth = -50f;
            tabletCamera.clearFlags = CameraClearFlags.SolidColor;
            tabletCamera.backgroundColor = Color.black;
            tabletCamera.cullingMask = ~(1 << 18);
            tabletCamera.enabled = IsLivePreviewMode();

            if (tabletCamera.targetTexture == null)
            {
                RenderTexture rt = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1,
                    filterMode = FilterMode.Bilinear,
                    name = "TUP_CameraPreview"
                };
                rt.Create();
                tabletCamera.targetTexture = rt;
                BindPreviewTexture(rt);
            }
            else
            {
                BindPreviewTexture(tabletCamera.targetTexture);
            }

            ApplyPreviewIdleVisual();
        }

        private void ApplyPreviewIdleVisual()
        {
            bool bundle = CameraTabletAssets.IsBundleTablet(cameraTabletT);
            // Bundle tablet always shows through-lens (never black out the viewfinder).
            bool live = bundle || IsLivePreviewMode();
            if (tabletCamera != null)
            {
                tabletCamera.enabled = live;
                tabletCamera.cullingMask = ~(1 << 18);
                tabletCamera.clearFlags = CameraClearFlags.SolidColor;
                tabletCamera.backgroundColor = Color.black;
            }

            if (mainPage?.PreviewImage != null)
            {
                if (live && tabletCamera != null && tabletCamera.targetTexture != null)
                {
                    mainPage.PreviewImage.texture = tabletCamera.targetTexture;
                    mainPage.PreviewImage.color = Color.white;
                    if (bundle)
                        CameraTabletAssets.BindPreviewTargets(cameraTabletT, tabletCamera.targetTexture);
                }
                else if (!bundle)
                {
                    mainPage.PreviewImage.texture = null;
                    mainPage.PreviewImage.color = Color.black;
                    if (tabletCamera != null && tabletCamera.targetTexture != null)
                        ClearRenderTextureBlack(tabletCamera.targetTexture);
                }
            }

            if (mainPage?.RecDot != null)
                mainPage.RecDot.gameObject.SetActive(IsLivePreviewMode());

            UpdateModeHint();
        }

        private void UpdateModeHint()
        {
            if (mainPage?.ModeHintText == null)
                return;

            string hint;
            switch (cameraMode)
            {
                case CameraMode.FirstPersonView:
                    hint = "POV active";
                    break;
                case CameraMode.ThirdPerson:
                    hint = "3RD PERSON active";
                    break;
                case CameraMode.FollowPlayer:
                    hint = "FOLLOW active";
                    break;
                case CameraMode.FreeCam:
                    hint = "FREECAM - WASD + arrows";
                    break;
                default:
                    hint = "IDLE - preview off";
                    break;
            }

            mainPage.ModeHintText.text = hint;
        }

        private static void ClearRenderTextureBlack(RenderTexture rt)
        {
            if (rt == null)
                return;
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = prev;
        }

        private void SyncPreviewCameraFromThirdPerson()
        {
            if (tabletCamera == null || thirdPersonCamera == null)
                return;

            tabletCamera.cullingMask = thirdPersonCamera.cullingMask & ~(1 << 18);
            tabletCamera.clearFlags = CameraClearFlags.SolidColor;
            tabletCamera.backgroundColor = Color.black;
            tabletCamera.farClipPlane = thirdPersonCamera.farClipPlane;
            tabletCamera.stereoTargetEye = StereoTargetEyeMask.None;

            // Keep PC / shoulder camera rendering to the game view, not the tablet RT.
            if (thirdPersonCamera.targetTexture != null &&
                tabletCamera.targetTexture != null &&
                ReferenceEquals(thirdPersonCamera.targetTexture, tabletCamera.targetTexture))
            {
                thirdPersonCamera.targetTexture = null;
            }
        }

        private void BindPreviewTexture(Texture texture)
        {
            if (cameraTabletT == null || texture == null)
                return;

            if (CameraTabletAssets.IsBundleTablet(cameraTabletT))
            {
                CameraTabletAssets.BindPreviewTargets(cameraTabletT, texture);
                return;
            }

            Transform screen = cameraTabletT.Find("MainPage/CameraScreen/Preview");
            if (screen == null)
                screen = cameraTabletT.Find("MainPage/CameraScreen");
            if (screen == null)
                return;

            UnityEngine.UI.RawImage raw = screen.GetComponent<UnityEngine.UI.RawImage>()
                                         ?? screen.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
            if (raw != null)
            {
                raw.texture = texture;
                // Caller decides idle vs live color via ApplyPreviewIdleVisual.
                if (cameraMode != CameraMode.None)
                    raw.color = Color.white;
            }

            MeshRenderer mesh = screen.GetComponent<MeshRenderer>()
                                ?? screen.GetComponentInChildren<MeshRenderer>(true);
            if (mesh != null && mesh.sharedMaterial != null)
            {
                Material mat = mesh.material;
                if (mat.HasProperty("_MainTex"))
                    mat.mainTexture = texture;
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", texture);
            }
        }

        private void SetupColorScreen()
        {
            if (colorScreenGo == null)
                return;

            void SetColor(Color color)
            {
                for (int i = 0; i < screenMats.Count; i++)
                {
                    if (screenMats[i] != null)
                        screenMats[i].color = color;
                }
            }

            Transform stuff = colorScreenGo.transform.Find("Stuff");
            if (stuff != null)
            {
                if (stuff.Find("RedButton") != null) Button(stuff.Find("RedButton"), () => SetColor(Color.red));
                if (stuff.Find("GreenButton") != null) Button(stuff.Find("GreenButton"), () => SetColor(Color.green));
                if (stuff.Find("BlueButton") != null) Button(stuff.Find("BlueButton"), () => SetColor(Color.blue));
            }

            foreach (string screenName in new[] { "Screen1", "Screen2", "Screen3" })
            {
                Transform screen = colorScreenGo.transform.Find(screenName);
                MeshRenderer rend = screen != null ? screen.GetComponent<MeshRenderer>() : null;
                if (rend != null)
                    screenMats.Add(rend.material);
            }

            colorScreenGo.transform.position = new Vector3(-54.3f, 16.21f, -122.96f);
            colorScreenGo.transform.Rotate(0, 30, 0);
            colorScreenGo.SetActive(false);
        }

        private static Transform FindThirdPersonCamera()
        {
            string[] paths =
            {
                "Player Objects/Third Person Camera/Shoulder Camera",
                "Player Objects/ThirdPersonCamera/Shoulder Camera",
                "Third Person Camera/Shoulder Camera"
            };
            for (int i = 0; i < paths.Length; i++)
            {
                GameObject go = GameObject.Find(paths[i]);
                if (go != null)
                    return go.transform;
            }

            foreach (UnityEngine.Camera cam in UnityEngine.Object.FindObjectsOfType<UnityEngine.Camera>(true))
            {
                if (cam == null) continue;
                string n = cam.gameObject.name ?? "";
                if (n.IndexOf("Shoulder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Third Person", StringComparison.OrdinalIgnoreCase) >= 0)
                    return cam.transform;
            }

            return null;
        }

        private static Transform FindCameraFollower()
        {
            string[] paths =
            {
                "Player Objects/Player VR Controller/GorillaPlayer/TurnParent/Main Camera/Camera Follower",
                "Player Objects/PlayerVRController/GorillaPlayer/TurnParent/Main Camera/Camera Follower"
            };
            for (int i = 0; i < paths.Length; i++)
            {
                GameObject go = GameObject.Find(paths[i]);
                if (go != null)
                    return go.transform;
            }

            Transform head = GTPlayer.Instance != null ? GTPlayer.Instance.headCollider?.transform : null;
            if (head != null)
                return head;

            return GorillaTagger.Instance != null ? GorillaTagger.Instance.mainCamera?.transform : null;
        }

        private CinemachineVirtualCamera _shoulderVcam;
        private bool _cmSuppressed;

        private CinemachineVirtualCamera FindShoulderVcam()
        {
            if (_shoulderVcam != null)
                return _shoulderVcam;
            if (thirdPersonCameraT == null)
                return null;

            Transform vcam = thirdPersonCameraT.Find("CM vcam1");
            if (vcam == null)
            {
                foreach (Transform t in thirdPersonCameraT.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && t.name.IndexOf("vcam", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        vcam = t;
                        break;
                    }
                }
            }

            if (vcam != null)
                _shoulderVcam = vcam.GetComponent<CinemachineVirtualCamera>();
            return _shoulderVcam;
        }

        private void SetCinemachineSuppressed(bool suppress)
        {
            var cm = FindShoulderVcam();
            if (cm == null)
                return;

            if (suppress)
            {
                if (!_cmSuppressed)
                {
                    cm.enabled = false;
                    _cmSuppressed = true;
                }
            }
            else if (_cmSuppressed)
            {
                cm.enabled = true;
                _cmSuppressed = false;
            }
        }

        [Obsolete("Use SetCinemachineSuppressed")]
        private static void DisableCinemachine(Transform shoulder)
        {
            // Kept for call-site compatibility during refactor — prefer SetCinemachineSuppressed.
            if (shoulder == null) return;
            Transform vcam = shoulder.Find("CM vcam1");
            if (vcam == null)
            {
                foreach (Transform t in shoulder.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.IndexOf("vcam", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        vcam = t;
                        break;
                    }
                }
            }
            if (vcam == null) return;
            var cm = vcam.GetComponent<CinemachineVirtualCamera>();
            if (cm != null)
                cm.enabled = false;
        }

        public void Flip()
        {
            if (LensT == null)
                return;
            isFaceCamera = !isFaceCamera;
            // Only flip the free lens — tablet GUI never rotates with the view.
            LensT.Rotate(0f, 180f, 0f);
            AlignLensCameras();
        }

        public void EnableFPV()
        {
            if (!init)
                return;
            if (UI.Instance != null)
                UI.Instance.freecam = false;
            if (isFaceCamera)
                Flip();
            cameraMode = CameraMode.FirstPersonView;
            ResetPoseFilters();
            AlignLensCameras();
            ApplyPreviewIdleVisual();
            EnsureTabletGuiVisible();
        }

        public void EnableTPV()
        {
            if (!init)
                return;

            if (UI.Instance != null)
                UI.Instance.freecam = false;

            if (tpvMode == TpvModes.Back)
            {
                if (isFaceCamera)
                    Flip();
            }
            else if (tpvMode == TpvModes.Front)
            {
                if (!isFaceCamera)
                    Flip();
            }

            cameraMode = CameraMode.ThirdPerson;
            ResetPoseFilters();
            AlignLensCameras();
            ApplyPreviewIdleVisual();
            EnsureTabletGuiVisible();
        }

        public void EnableFollow()
        {
            if (!init)
                return;

            if (UI.Instance != null)
                UI.Instance.freecam = false;

            cameraMode = cameraMode == CameraMode.FollowPlayer ? CameraMode.None : CameraMode.FollowPlayer;
            ResetPoseFilters();
            if (cameraMode == CameraMode.None)
                BringTabletToPlayer();
            else
                AlignLensCameras();
            ApplyPreviewIdleVisual();
            EnsureTabletGuiVisible();
        }

        /// <summary>
        /// When false (default), FreeCam/POV only updates the tablet preview RT.
        /// Never hijacks GT Shoulder / PC monitor — that caused permanent "super zoom".
        /// </summary>
        public bool DrivePcMonitor;

        private Transform pcCamOriginalParent;
        private Vector3 pcCamOriginalLocalPos;
        private Quaternion pcCamOriginalLocalRot;
        private bool pcCamDriven;

        private bool gtDefaultsCaptured;
        private Transform gtParent;
        private Vector3 gtLocalPos;
        private Quaternion gtLocalRot;
        private float gtFov = 80f;
        private float gtNear = 0.07f;
        private float gtFar = 1000f;

        private void CaptureGtPcCameraDefaults()
        {
            if (gtDefaultsCaptured || thirdPersonCameraT == null || thirdPersonCamera == null)
                return;

            gtParent = thirdPersonCameraT.parent;
            gtLocalPos = thirdPersonCameraT.localPosition;
            gtLocalRot = thirdPersonCameraT.localRotation;
            gtFov = thirdPersonCamera.fieldOfView;
            if (gtFov < 40f || gtFov > 120f)
                gtFov = 80f;
            gtNear = Mathf.Clamp(thirdPersonCamera.nearClipPlane, 0.01f, 0.35f);
            gtFar = thirdPersonCamera.farClipPlane > 1f ? thirdPersonCamera.farClipPlane : 1000f;
            gtDefaultsCaptured = true;
            Debug.Log($"[TUP Camera] Captured GT PC cam defaults FOV={gtFov:0} near={gtNear:0.##}");
        }

        /// <summary>Hard restore Shoulder / PC monitor to Gorilla Tag third-person — call sparingly, not every frame.</summary>
        public void RestoreGtPcCamera()
        {
            if (thirdPersonCamera == null || thirdPersonCameraT == null)
                return;

            pcCamDriven = false;
            SetCinemachineSuppressed(false);

            try
            {
                if (gtDefaultsCaptured && gtParent != null)
                {
                    thirdPersonCameraT.SetParent(gtParent, false);
                    thirdPersonCameraT.localPosition = gtLocalPos;
                    thirdPersonCameraT.localRotation = gtLocalRot;
                }
                else if (pcCamOriginalParent != null)
                {
                    thirdPersonCameraT.SetParent(pcCamOriginalParent, false);
                    thirdPersonCameraT.localPosition = pcCamOriginalLocalPos;
                    thirdPersonCameraT.localRotation = pcCamOriginalLocalRot;
                }
            }
            catch { /* parent destroyed */ }

            thirdPersonCamera.targetTexture = null;

            // THE FIX: desktop (no HMD) monitor is rendered by Main Camera (depth 0).
            // Shoulder (depth 2) must be OFF or it overdraws the monitor zoomed-in.
            if (IsDesktopNoHmd())
            {
                thirdPersonCamera.enabled = false;
            }
            else
            {
                // VR: GT drives Shoulder for the mirror view — restore its numbers only.
                if (gtDefaultsCaptured)
                {
                    thirdPersonCamera.fieldOfView = Mathf.Max(gtFov, 70f);
                    thirdPersonCamera.nearClipPlane = gtNear;
                    thirdPersonCamera.farClipPlane = gtFar;
                }
                else
                {
                    thirdPersonCamera.fieldOfView = Mathf.Max(thirdPersonCamera.fieldOfView, 70f);
                    if (thirdPersonCamera.fieldOfView > 120f)
                        thirdPersonCamera.fieldOfView = 80f;
                    thirdPersonCamera.nearClipPlane = Mathf.Clamp(thirdPersonCamera.nearClipPlane, 0.01f, 0.35f);
                }
            }

            var cm = FindShoulderVcam();
            if (cm != null)
                cm.enabled = true;
        }

        public static bool IsDesktopNoHmd()
        {
            try { return !UnityEngine.XR.XRSettings.isDeviceActive; }
            catch { return true; }
        }

        /// <summary>
        /// One-shot PC monitor repair. Desktop root cause: Shoulder Camera (depth 2)
        /// force-enabled over Main Camera (depth 0) = zoomed monitor. Fix = disable it.
        /// </summary>
        public void ForceFixZoomedPcView()
        {
            DrivePcMonitor = false;
            PlayerPrefs.SetInt("TUP_DrivePcMonitor", 0);
            cameraMode = CameraMode.None;
            if (UI.Instance != null)
                UI.Instance.freecam = false;

            RestoreGtPcCamera();

            if (tabletCamera != null)
                tabletCamera.enabled = false;

            ApplyPreviewIdleVisual();
            Debug.Log($"[TUP Camera] ForceFixZoomedPcView — desktop={IsDesktopNoHmd()} shoulderEnabled={(thirdPersonCamera != null && thirdPersonCamera.enabled)}");
        }

        private void AlignLensCameras()
        {
            if (LensT == null)
                return;

            if (tabletCamera != null)
            {
                tabletCamera.cullingMask = ~(1 << 18);
                tabletCamera.clearFlags = CameraClearFlags.SolidColor;
                tabletCamera.backgroundColor = Color.black;
                // Bundle: always render through the viewfinder. Procedural: live modes only.
                bool live = CameraTabletAssets.IsBundleTablet(cameraTabletT) || IsLivePreviewMode();
                tabletCamera.enabled = live;
                if (tabletCamera.targetTexture != null && live)
                    BindPreviewTexture(tabletCamera.targetTexture);
            }

            if (ShouldDrivePcMonitor())
                SyncPcCameraFromLens();
            else
                RestoreGtPcCamera();
        }

        /// <summary>
        /// FreeCam always drives the PC monitor. Other modes respect DrivePcMonitor.
        /// </summary>
        public bool ShouldDrivePcMonitor()
        {
            if (cameraMode == CameraMode.FreeCam)
                return true;
            return DrivePcMonitor && IsLivePreviewMode();
        }

        /// <summary>
        /// Copy lens → GT Shoulder so the whole PC / monitor follows FreeCam (or POV when Drive is on).
        /// When not driving, only restore ONCE if we had previously hijacked — never
        /// every frame (that fought Cinemachine and froze a zoomed/close pose).
        /// </summary>
        public void SyncPcCameraFromLens()
        {
            if (thirdPersonCamera == null || thirdPersonCameraT == null)
                return;

            if (!ShouldDrivePcMonitor() || LensT == null)
            {
                if (pcCamDriven || _cmSuppressed)
                    RestoreGtPcCamera();
                return;
            }

            CapturePcCameraHierarchyOnce();
            SetCinemachineSuppressed(true);

            thirdPersonCamera.targetTexture = null;
            thirdPersonCamera.enabled = true;
            thirdPersonCameraT.SetPositionAndRotation(LensT.position, LensT.rotation);

            if (tabletCamera != null)
            {
                thirdPersonCamera.fieldOfView = tabletCamera.fieldOfView;
                thirdPersonCamera.nearClipPlane = Mathf.Max(0.01f, tabletCamera.nearClipPlane);
                thirdPersonCamera.farClipPlane = tabletCamera.farClipPlane;
            }
        }

        private void CapturePcCameraHierarchyOnce()
        {
            if (pcCamDriven || thirdPersonCameraT == null)
                return;

            if (gtDefaultsCaptured)
            {
                pcCamOriginalParent = gtParent;
                pcCamOriginalLocalPos = gtLocalPos;
                pcCamOriginalLocalRot = gtLocalRot;
            }
            else
            {
                pcCamOriginalParent = thirdPersonCameraT.parent;
                pcCamOriginalLocalPos = thirdPersonCameraT.localPosition;
                pcCamOriginalLocalRot = thirdPersonCameraT.localRotation;
            }

            thirdPersonCameraT.SetParent(null, true);
            pcCamDriven = true;
        }

        private void RestorePcCameraHierarchy()
        {
            RestoreGtPcCamera();
        }

        public void InitCosmeticsHider()
        {
            if (isCosmeticsHiderInited || !init || thirdPersonCameraT == null || cameraTabletT == null)
                return;

            isCosmeticsHiderInited = true;
            HeadCosmeticsHider = thirdPersonCameraT.gameObject.GetComponent<HeadCosmeticsHider>()
                                 ?? thirdPersonCameraT.gameObject.AddComponent<HeadCosmeticsHider>();
            HeadCosmeticsHider.enabled = false;

            Transform hideBtn = cameraTabletT.Find("MainPage/HideHeadCosmetics");
            if (hideBtn == null)
                return;

            ToggleButton toggle = hideBtn.gameObject.GetComponent<ToggleButton>()
                                  ?? hideBtn.gameObject.AddComponent<ToggleButton>();
            toggle.InitToggleButton(
                b =>
                {
                    if (HeadCosmeticsHider != null)
                        HeadCosmeticsHider.enabled = b;
                },
                () => HeadCosmeticsHider != null && HeadCosmeticsHider.enabled,
                savable: true,
                overrideSaveName: "TUP_CameraHideHats");
            if (!buttons.Contains(toggle))
                buttons.Add(toggle);
        }

        public void ResetEverythingPcView()
        {
            ForceFixZoomedPcView();
        }

        public void PlaceTabletBesidePlayer()
        {
            if (!init || cameraTabletT == null)
                return;

            cameraTabletT.parent = null;
            cameraTabletT.gameObject.SetActive(true);
            SetTabletVisibility(true);

            Transform head = null;
            if (GTPlayer.Instance != null && GTPlayer.Instance.headCollider != null)
                head = GTPlayer.Instance.headCollider.transform;
            else if (GorillaTagger.Instance != null && GorillaTagger.Instance.mainCamera != null)
                head = GorillaTagger.Instance.mainCamera.transform;
            if (head == null)
                return;

            // Side + back of view — never park 0.65m in front of the eyes (fills PC cam).
            Vector3 pos = head.position + head.right * 0.85f + head.forward * 0.35f + head.up * 0.05f;
            cameraTabletT.position = pos;
            cameraTabletT.rotation = Quaternion.LookRotation(head.position - pos, Vector3.up);
            cameraTabletT.Rotate(0f, 180f, 0f);
        }

        public void BringTabletToPlayer()
        {
            BringTabletToPlayer(exitModes: true);
        }

        public void SummonTabletGuiOnly()
        {
            BringTabletToPlayer(exitModes: false);
        }

        public void BringTabletToPlayer(bool exitModes)
        {
            if (!init || cameraTabletT == null)
                return;

            if (exitModes)
            {
                cameraMode = CameraMode.None;
                if (UI.Instance != null)
                    UI.Instance.freecam = false;
                ResetPoseFilters();
                RestorePcCameraHierarchy();
                ApplyPreviewIdleVisual();
            }

            cameraTabletT.parent = null;
            cameraTabletT.gameObject.SetActive(true);

            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] != null)
                    buttons[i].gameObject.SetActive(true);
            }

            SetTabletVisibility(true);
            if (mainPage?.GO != null)
                mainPage.GO.SetActive(true);
            if (miscPage?.GO != null)
                miscPage.GO.SetActive(false);
            if (povPage?.GO != null)
                povPage.GO.SetActive(false);
            if (controlsPage?.GO != null)
                controlsPage.GO.SetActive(false);

            Transform head = null;
            if (GTPlayer.Instance != null && GTPlayer.Instance.headCollider != null)
                head = GTPlayer.Instance.headCollider.transform;
            else if (GorillaTagger.Instance != null && GorillaTagger.Instance.mainCamera != null)
                head = GorillaTagger.Instance.mainCamera.transform;

            if (head == null)
                return;

            bool desktop = false;
            try { desktop = !UnityEngine.XR.XRSettings.isDeviceActive; }
            catch { desktop = true; }

            if (desktop)
            {
                // Keep tablet out of the monitor frustum on flat-screen.
                Vector3 pos = head.position + head.right * 0.85f + head.forward * 0.35f + head.up * 0.05f;
                cameraTabletT.position = pos;
                cameraTabletT.rotation = Quaternion.LookRotation(head.position - pos, Vector3.up);
                cameraTabletT.Rotate(0f, 180f, 0f);
            }
            else
            {
                cameraTabletT.position = head.position + head.forward * 0.65f;
                cameraTabletT.LookAt(head.position);
                cameraTabletT.Rotate(0f, -180f, 0f);
            }
        }

        public void RegisterButtons()
        {
            AddTabletButton("MiscPage/BackButton", () =>
            {
                ShowOnlyPage(mainPage?.GO);
            });

            AddTabletButton("MainPage/MiscButton", () =>
            {
                ShowOnlyPage(miscPage?.GO);
            });

            AddTabletButton("MiscPage/ControlsButton", () =>
            {
                RefreshControlsBindText();
                ShowOnlyPage(controlsPage?.GO);
            });

            AddTabletButton("ControlsPage/CtrlBackButton", () =>
            {
                ShowOnlyPage(miscPage?.GO);
            });

            AddTabletButton("ControlsPage/BindCycleButton", () =>
            {
                CameraMod.Binds.CycleActivateBind();
                RefreshControlsBindText();
            });

            AddHoldableTabletButton("ControlsPage/CtrlSmoothUp", () =>
            {
                ChangeSmoothing(0.01f);
                RefreshControlsStatusTexts();
            });
            AddHoldableTabletButton("ControlsPage/CtrlSmoothDown", () =>
            {
                ChangeSmoothing(-0.01f);
                RefreshControlsStatusTexts();
            });

            AddTabletButton("MainPage/PovOptsButton", () =>
            {
                RefreshPovOffsetTexts();
                ShowOnlyPage(povPage?.GO);
            });

            AddTabletButton("PovPage/PovBackButton", () =>
            {
                ShowOnlyPage(mainPage?.GO);
            });

            AddTabletButton("PovPage/EnterPovButton", EnableFPV);

            AddTabletButton("PovPage/PovResetButton", ResetPovOffsets);

            AddHoldableTabletButton("PovPage/ZoomUp", () => ChangeFov(2));
            AddHoldableTabletButton("PovPage/ZoomDown", () => ChangeFov(-2));
            AddHoldableTabletButton("PovPage/PovSmoothUp", () => ChangeSmoothing(0.01f));
            AddHoldableTabletButton("PovPage/PovSmoothDown", () => ChangeSmoothing(-0.01f));
            AddHoldableTabletButton("PovPage/PovNearUp", () => ChangeNearClip(0.01f));
            AddHoldableTabletButton("PovPage/PovNearDown", () => ChangeNearClip(-0.01f));
            AddHoldableTabletButton("PovPage/HeightUp", () => ChangePovOffset(1, 0.02f));
            AddHoldableTabletButton("PovPage/HeightDown", () => ChangePovOffset(1, -0.02f));
            AddHoldableTabletButton("PovPage/DistUp", () => ChangePovOffset(2, 0.02f));
            AddHoldableTabletButton("PovPage/DistDown", () => ChangePovOffset(2, -0.02f));
            AddHoldableTabletButton("PovPage/SideUp", () => ChangePovOffset(0, 0.02f));
            AddHoldableTabletButton("PovPage/SideDown", () => ChangePovOffset(0, -0.02f));
            AddHoldableTabletButton("PovPage/StableUp", () => ChangePovStability(0.1f));
            AddHoldableTabletButton("PovPage/StableDown", () => ChangePovStability(-0.1f));

            AddTabletButton("MainPage/FPVButton", EnableFPV);
            AddHoldableTabletButton("MainPage/SmoothingDownButton", () => ChangeSmoothing(-0.01f));
            AddHoldableTabletButton("MainPage/SmoothingUpButton", () => ChangeSmoothing(0.01f));
            AddTabletButton("MainPage/FovUP", () => ChangeFov(5));
            AddTabletButton("MainPage/FovDown", () => ChangeFov(-5));
            AddHoldableTabletButton("MainPage/NearClipUp", () => ChangeNearClip(0.01f));
            AddHoldableTabletButton("MainPage/NearClipDown", () => ChangeNearClip(-0.01f));
            AddTabletButton("MainPage/FlipCamButton", Flip);
            AddTabletButton("MainPage/FPButton", EnableFollow);

            Transform roll = cameraTabletT.Find("MainPage/RollLock");
            if (roll != null)
            {
                buttons.Add(roll.gameObject.AddComponent<ToggleButton>()
                    .InitToggleButton(b => RollLock = b, () => RollLock, savable: true, overrideSaveName: "TUP_CameraRollLock"));
            }

            AddTabletButton("MainPage/TPVButton", EnableTPV);

            AddTabletButton("MiscPage/MinDistDownButton", () =>
            {
                minDist = Mathf.Max(1f, minDist - 0.1f);
                if (miscPage?.MinDistText != null)
                    miscPage.MinDistText.text = minDist.ToString("#.##");
            });
            AddTabletButton("MiscPage/MinDistUpButton", () =>
            {
                minDist = Mathf.Min(10f, minDist + 0.1f);
                if (miscPage?.MinDistText != null)
                    miscPage.MinDistText.text = minDist.ToString("#.##");
            });
            AddTabletButton("MiscPage/SpeedUpButton", () =>
            {
                fpspeed = Mathf.Min(0.1f, fpspeed + 0.01f);
                if (miscPage?.SpeedText != null)
                    miscPage.SpeedText.text = fpspeed.ToString("#.##");
            });
            AddTabletButton("MiscPage/SpeedDownButton", () =>
            {
                fpspeed = Mathf.Max(0.01f, fpspeed - 0.01f);
                if (miscPage?.SpeedText != null)
                    miscPage.SpeedText.text = fpspeed.ToString("#.##");
            });
            AddTabletButton("MiscPage/TPModeDownButton", () =>
            {
                tpvMode = tpvMode == TpvModes.Back ? TpvModes.Front : TpvModes.Back;
                if (miscPage?.TpText != null)
                    miscPage.TpText.text = tpvMode.ToString();
            });
            AddTabletButton("MiscPage/TPModeUpButton", () =>
            {
                tpvMode = tpvMode == TpvModes.Back ? TpvModes.Front : TpvModes.Back;
                if (miscPage?.TpText != null)
                    miscPage.TpText.text = tpvMode.ToString();
            });
            AddTabletButton("MiscPage/TPRotButton", () =>
            {
                followheadrot = !followheadrot;
                if (miscPage?.TpRotText != null)
                    miscPage.TpRotText.text = followheadrot.ToString().ToUpper();
            });

            Transform gs = cameraTabletT.Find("MiscPage/GreenScreenButton");
            if (gs != null && colorScreenGo != null)
            {
                buttons.Add(gs.gameObject.AddComponent<ToggleButton>()
                    .InitToggleButton(
                        b => colorScreenGo.SetActive(b),
                        () => colorScreenGo.activeSelf,
                        savable: true,
                        overrideSaveName: "TUP_CameraGreenScreen"));
            }

            Transform keepModeBtn = cameraTabletT.Find("MiscPage/KeepModeButton");
            if (keepModeBtn != null)
            {
                buttons.Add(keepModeBtn.gameObject.AddComponent<ToggleButton>()
                    .InitToggleButton(
                        b =>
                        {
                            KeepModeOnPopOut = b;
                            PlayerPrefs.SetInt("TUP_CameraKeepMode", b ? 1 : 0);
                        },
                        () => KeepModeOnPopOut,
                        savable: true,
                        overrideSaveName: "TUP_CameraKeepMode"));
            }

            Transform clampBtn = cameraTabletT.Find("MiscPage/AngleClampButton");
            if (clampBtn != null)
            {
                buttons.Add(clampBtn.gameObject.AddComponent<ToggleButton>()
                    .InitToggleButton(
                        b => AngleClamping = b,
                        () => AngleClamping,
                        savable: true,
                        overrideSaveName: "AngleClamping"));
            }

            WireKeepGuiToggle("MiscPage/KeepGuiButton");
            WireKeepGuiToggle("PovPage/PovKeepGuiButton");
            WireKeepGuiToggle("ControlsPage/CtrlKeepGuiButton");
            WireSmoothOffToggle("MiscPage/SmoothOffButton");
            WireSmoothOffToggle("PovPage/PovSmoothOffButton");
            WireSmoothOffToggle("ControlsPage/CtrlSmoothOffButton");

            Transform skins = cameraTabletT.Find("MiscPage/Skins");
            if (skins != null)
            {
                foreach (Transform child in skins)
                    AddTabletButton("MiscPage/Skins/" + child.name, () => SetSkin(child.name));
            }
        }

        private void WireKeepGuiToggle(string path)
        {
            Transform t = cameraTabletT != null ? cameraTabletT.Find(path) : null;
            if (t == null)
                return;
            buttons.Add(t.gameObject.AddComponent<ToggleButton>()
                .InitToggleButton(
                    b =>
                    {
                        KeepGuiInActiveModes = b;
                        PlayerPrefs.SetInt("TUP_CameraKeepGui", b ? 1 : 0);
                        SyncAllToggleAppearances();
                        RefreshControlsStatusTexts();
                    },
                    () => KeepGuiInActiveModes,
                    savable: true,
                    overrideSaveName: "TUP_CameraKeepGui"));
        }

        private void WireSmoothOffToggle(string path)
        {
            Transform t = cameraTabletT != null ? cameraTabletT.Find(path) : null;
            if (t == null)
                return;
            // Toggle ON means smoothing disabled ("NO SMOOTH").
            buttons.Add(t.gameObject.AddComponent<ToggleButton>()
                .InitToggleButton(
                    b =>
                    {
                        SetSmoothingEnabled(!b);
                        SyncAllToggleAppearances();
                    },
                    () => !SmoothingEnabled,
                    savable: true,
                    overrideSaveName: "TUP_CameraNoSmooth"));
        }

        public ClickButton Button(Transform t, Action onClick)
        {
            if (t == null)
                return null;
            ClickButton button = t.gameObject.AddComponent<ClickButton>();
            button.OnClick(onClick);
            return button;
        }

        public void AddTabletButton(string relativeButtonPath, Action onClick)
        {
            Transform t = ResolveTabletButton(relativeButtonPath);
            if (t == null)
            {
                Debug.LogWarning("[TUP Camera] Missing button: " + relativeButtonPath);
                return;
            }
            buttons.Add(Button(t, onClick));
        }

        public void AddHoldableTabletButton(string buttonPath, Action onClick)
        {
            Transform t = ResolveTabletButton(buttonPath);
            if (t == null)
            {
                Debug.LogWarning("[TUP Camera] Missing hold button: " + buttonPath);
                return;
            }
            buttons.Add(t.gameObject.AddComponent<HoldableButton>().OnClick(onClick));
        }

        private Transform ResolveTabletButton(string relativeButtonPath)
        {
            if (cameraTabletT == null || string.IsNullOrEmpty(relativeButtonPath))
                return null;

            if (CameraTabletAssets.IsBundleTablet(cameraTabletT))
                return CameraTabletAssets.ResolveButtonHit(cameraTabletT, relativeButtonPath);

            return cameraTabletT.Find(relativeButtonPath);
        }

        private enum BundleSettingSlot { Fov = 0, Smooth = 1, Near = 2 }
        private BundleSettingSlot _bundleSettingSlot;

        private void RegisterBundleTabletButtons()
        {
            const string settings = "Main/MainView/SettingsHolder/MainSettings/";
            const string ext = "Main/MainView/SettingsHolder/ExtSettings/";

            AddTabletButton(settings + "1ST-PERSON", EnableFPV);
            AddTabletButton(settings + "3RD-PERSON", EnableTPV);
            AddTabletButton(settings + "FOLLOW", EnableFollow);
            AddTabletButton(settings + "GOPRO", EnableFreeCam);
            AddTabletButton(settings + "LOCK", () =>
            {
                RollLock = !RollLock;
                RefreshBundleSettingReadout();
            });

            AddHoldableTabletButton(ext + "IncUp", () => AdjustBundleSetting(1));
            AddHoldableTabletButton(ext + "IncDown", () => AdjustBundleSetting(-1));
            AddTabletButton(ext + "SetNext", () =>
            {
                _bundleSettingSlot = (BundleSettingSlot)(((int)_bundleSettingSlot + 1) % 3);
                RefreshBundleSettingReadout();
            });
            AddTabletButton(ext + "SetDown", () =>
            {
                int next = ((int)_bundleSettingSlot + 2) % 3;
                _bundleSettingSlot = (BundleSettingSlot)next;
                RefreshBundleSettingReadout();
            });

            AddTabletButton("Main/MainView/SettingsHolder/SettingsExpand", () =>
            {
                Transform extSettings = CameraTabletAssets.FindDeep(cameraTabletT, "ExtSettings");
                if (extSettings != null)
                {
                    bool show = !extSettings.gameObject.activeSelf;
                    extSettings.gameObject.SetActive(show);
                    // Never leave a black expand plate covering the viewfinder.
                    CameraTabletAssets.PrepBundleScreen(cameraTabletT);
                }
            });

            RefreshBundleSettingReadout();
            Debug.Log("[TUP Camera] Bundle tablet buttons wired (POV/3RD/Follow/GoPro/FOV).");
        }

        private void AdjustBundleSetting(int dir)
        {
            switch (_bundleSettingSlot)
            {
                case BundleSettingSlot.Fov:
                    ChangeFov(dir > 0 ? 5 : -5);
                    break;
                case BundleSettingSlot.Smooth:
                    ChangeSmoothing(dir > 0 ? 0.01f : -0.01f);
                    break;
                case BundleSettingSlot.Near:
                    ChangeNearClip(dir > 0 ? 0.01f : -0.01f);
                    break;
            }
            RefreshBundleSettingReadout();
        }

        private void RefreshBundleSettingReadout()
        {
            if (mainPage == null)
                return;

            string title;
            string value;
            switch (_bundleSettingSlot)
            {
                case BundleSettingSlot.Smooth:
                    title = "SMOOTH";
                    value = SmoothingEnabled ? smoothing.ToString("0.##") : "OFF";
                    break;
                case BundleSettingSlot.Near:
                    title = "NEAR";
                    value = tabletCamera != null ? tabletCamera.nearClipPlane.ToString("0.##") : "—";
                    break;
                default:
                    title = "FOV";
                    value = tabletCamera != null ? tabletCamera.fieldOfView.ToString("0") : "—";
                    break;
            }

            if (mainPage.SettingTitle != null)
                mainPage.SettingTitle.text = title;
            if (mainPage.FOVText != null)
                mainPage.FOVText.text = value;
        }

        public void SetTabletVisibility(bool visible)
        {
            if (appearance?.go != null)
                appearance.go.SetActive(visible);

            void SetChild(string name, bool on)
            {
                Transform t = cameraTabletT != null ? cameraTabletT.Find(name) : null;
                if (t != null)
                    t.gameObject.SetActive(on);
            }

            if (CameraTabletAssets.IsBundleTablet(cameraTabletT))
            {
                // Keep the whole asset tablet shell visible/hidden together.
                if (cameraTabletT != null)
                {
                    // Never disable root (breaks DontDestroy / follow). Toggle shell pieces.
                    string[] shell =
                    {
                        "MainScreen", "Border", "TopBar", "TopBarUnder", "UnderBar",
                        "PipeTop1", "PipeTop2", "Pole1", "Pole2", "BarConnector",
                        "GripMainL", "GripMainR", "GripAccentL", "GripAccentR",
                        "GripConnectL", "GripConnectR", "Main"
                    };
                    for (int i = 0; i < shell.Length; i++)
                        SetChild(shell[i], visible);
                }
            }
            else
            {
                SetChild("FakeCamera", visible);
                SetChild("FacePlate", visible);
                SetChild("AccentRail", visible);
                SetChild("LeftGrabCol", visible);
                SetChild("RightGrabCol", visible);
            }

            if (tabletCamera != null)
            {
                bool live = CameraTabletAssets.IsBundleTablet(cameraTabletT) || (visible && IsLivePreviewMode());
                tabletCamera.enabled = visible && live;
            }
            if (visible)
                ApplyPreviewIdleVisual();
        }

        private float lastPokeInvoke;
        private BaseButton lastPokedButton;

        private void PollTabletButtonPokes()
        {
            bool allowGuiPoke = cameraMode == CameraMode.None
                                || cameraMode == CameraMode.FreeCam
                                || KeepGuiInActiveModes;
            if (!allowGuiPoke || cameraTabletT == null || ButtonsTimeouted)
                return;
            if (Time.time - lastPokeInvoke < Mathf.Max(0.18f, ButtonCooldownSeconds))
                return;

            bool pageOpen = (mainPage?.GO != null && mainPage.GO.activeInHierarchy)
                            || (miscPage?.GO != null && miscPage.GO.activeInHierarchy)
                            || (povPage?.GO != null && povPage.GO.activeInHierarchy)
                            || (controlsPage?.GO != null && controlsPage.GO.activeInHierarchy);
            if (!pageOpen)
                return;

            Transform left = GorillaTagger.Instance?.leftHandTransform;
            Transform right = GorillaTagger.Instance?.rightHandTransform;
            if (left == null && right == null)
                return;

            const float pokeRadius = 0.11f;
            BaseButton best = null;
            float bestDist = pokeRadius;
            bool bestLeft = false;

            for (int i = 0; i < buttons.Count; i++)
            {
                BaseButton btn = buttons[i];
                if (btn == null || !btn.isActiveAndEnabled || !btn.gameObject.activeInHierarchy)
                    continue;

                Collider col = btn.GetComponent<Collider>();
                if (col == null || !col.enabled)
                    continue;

                Vector3 center = col.bounds.center;
                if (left != null)
                {
                    float d = Vector3.Distance(left.position, center);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = btn;
                        bestLeft = true;
                    }
                }

                if (right != null)
                {
                    float d = Vector3.Distance(right.position, center);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = btn;
                        bestLeft = false;
                    }
                }
            }

            if (best == null)
            {
                lastPokedButton = null;
                return;
            }

            if (best == lastPokedButton)
                return;

            lastPokedButton = best;
            lastPokeInvoke = Time.time;
            NotifyButtonPressed();
            best.Vibration(bestLeft);
            if (best is ToggleButton toggle)
                toggle.IsDown = !toggle.IsDown;
            else
                best.onClick?.Invoke();
        }

        public void AnUpdate()
        {
            ReplicationHandler.Lerp();
            if (!init || cameraTabletT == null)
                return;

            PollTabletButtonPokes();
            UpdateStabilizedPoses();
            // Keep idle plate clean if something re-enables the preview cam.
            if (cameraMode == CameraMode.None && tabletCamera != null && tabletCamera.enabled)
                ApplyPreviewIdleVisual();

            // Guard: never let the tablet RT steal the PC camera output.
            // Do NOT force-enable Shoulder here — on desktop it draws OVER Main Camera
            // (depth 2 vs 0) and jams the monitor into a zoomed close-up.
            if (thirdPersonCamera != null)
            {
                if (thirdPersonCamera.targetTexture != null)
                    thirdPersonCamera.targetTexture = null;

                bool shouldDrive = ShouldDrivePcMonitor();
                if (!shouldDrive && IsDesktopNoHmd() && thirdPersonCamera.enabled)
                    thirdPersonCamera.enabled = false; // Main Camera owns the monitor
                else if (shouldDrive && !thirdPersonCamera.enabled)
                    thirdPersonCamera.enabled = true;
            }

            if (cameraMode == CameraMode.FirstPersonView)
            {
                // Tablet GUI stays put — only the detached lens follows the head.
                EnsureTabletGuiVisible();

                if (cameraFollowerT != null && LensT != null)
                    ApplyStableHeadFollow(LensT, FirstPersonOffset, smoothing, RollLock);
            }

            if (BindEnabled && Binds.Tablet() && cameraTabletT.parent == null)
            {
                // Summon tablet GUI without killing the active camera mode.
                if (IsLivePreviewMode())
                    SummonTabletGuiOnly();
                else
                    BringTabletToPlayer();
            }

            if (cameraMode == CameraMode.FollowPlayer && cameraFollowerT != null && LensT != null)
            {
                EnsureTabletGuiVisible();
                if (!isFaceCamera)
                    Flip();

                Vector3 followTarget = filteredHeadPos;
                float dist = Vector3.Distance(followTarget, LensT.position);
                if (dist > minDist)
                {
                    float t = SmoothingEnabled
                        ? StableFollowT(Mathf.Max(2.5f, fpspeed * 28f))
                        : 1f;
                    LensT.position = Vector3.Lerp(LensT.position, followTarget, t);
                }

                Vector3 away = LensT.position - filteredHeadPos;
                if (away.sqrMagnitude > 0.0001f)
                    ApplySmoothedLook(LensT, Quaternion.LookRotation(away.normalized, Vector3.up));
            }

            if (cameraMode == CameraMode.ThirdPerson)
            {
                EnsureTabletGuiVisible();

                if (cameraFollowerT != null && tpvBodyFollowerT != null && LensT != null)
                {
                    Vector3 targetPosition;
                    Vector3 lookPoint;
                    switch (tpvMode)
                    {
                        case TpvModes.Back:
                            targetPosition = followheadrot
                                ? TransformPointStable(filteredHeadPos, filteredHeadRot, new Vector3(0.3f, 0.1f, -1.5f))
                                : TransformPointStable(filteredBodyPos, filteredBodyRot, new Vector3(0.3f, 0.1f, -1.5f));
                            lookPoint = filteredHeadPos;
                            break;
                        case TpvModes.Front:
                        default:
                            targetPosition = followheadrot
                                ? TransformPointStable(filteredHeadPos, filteredHeadRot, new Vector3(0.1f, 0.3f, 2.5f))
                                : TransformPointStable(filteredBodyPos, filteredBodyRot, new Vector3(0.1f, 0.3f, 2.5f));
                            lookPoint = 2f * targetPosition - filteredHeadPos;
                            break;
                    }

                    float smoothTime = SmoothingEnabled ? TpvSmoothTime : 0.02f;
                    float maxSpeed = SmoothingEnabled ? MaxSmoothSpeed : 25f;
                    LensT.position = Vector3.SmoothDamp(
                        LensT.position,
                        targetPosition,
                        ref velocity,
                        smoothTime,
                        maxSpeed,
                        Time.deltaTime);

                    if (velocity.sqrMagnitude > MaxSmoothSpeed * MaxSmoothSpeed)
                        velocity = velocity.normalized * MaxSmoothSpeed;

                    Vector3 lookDir = lookPoint - LensT.position;
                    if (lookDir.sqrMagnitude > 0.0001f)
                        ApplySmoothedLook(LensT, Quaternion.LookRotation(lookDir.normalized, Vector3.up));
                }

                if (Binds.Tablet())
                    SummonTabletGuiOnly();
            }

            // PC monitor: FreeCam always; other modes only while DrivePcMonitor is on.
            if (ShouldDrivePcMonitor())
                SyncPcCameraFromLens();
        }

        private void UpdateStabilizedPoses()
        {
            if (cameraFollowerT == null)
                return;

            if (!poseFilterReady || poseFilterMode != cameraMode)
            {
                filteredHeadPos = cameraFollowerT.position;
                filteredHeadRot = cameraFollowerT.rotation;
                if (tpvBodyFollowerT != null)
                {
                    filteredBodyPos = tpvBodyFollowerT.position;
                    filteredBodyRot = tpvBodyFollowerT.rotation;
                }
                else
                {
                    filteredBodyPos = filteredHeadPos;
                    filteredBodyRot = filteredHeadRot;
                }

                filteredLookRot = LensT != null ? LensT.rotation : filteredHeadRot;
                velocity = Vector3.zero;
                poseFilterReady = true;
                poseFilterMode = cameraMode;
            }

            float t = StableFollowT(HeadFilterHz * Mathf.Clamp(povStability, 0.35f, 2.5f));
            filteredHeadPos = FilterPosition(filteredHeadPos, cameraFollowerT.position, t, HeadPosDeadzone / Mathf.Max(0.5f, povStability));
            filteredHeadRot = FilterRotation(filteredHeadRot, cameraFollowerT.rotation, t, HeadAngDeadzoneDeg / Mathf.Max(0.5f, povStability));

            if (tpvBodyFollowerT != null)
            {
                // Body already damps a lot; lighter deadzone, still filtered.
                filteredBodyPos = FilterPosition(filteredBodyPos, tpvBodyFollowerT.position, t, HeadPosDeadzone * 0.5f);
                filteredBodyRot = FilterRotation(filteredBodyRot, tpvBodyFollowerT.rotation, t, HeadAngDeadzoneDeg * 0.65f);
            }
            else
            {
                filteredBodyPos = filteredHeadPos;
                filteredBodyRot = filteredHeadRot;
            }
        }

        private static Vector3 FilterPosition(Vector3 current, Vector3 raw, float t, float deadzone)
        {
            Vector3 delta = raw - current;
            float mag = delta.magnitude;
            if (mag < deadzone)
                return current;

            // Only chase past the deadzone so micro shakes never move the filtered pose.
            Vector3 softened = current + delta.normalized * (mag - deadzone * 0.55f);
            return Vector3.Lerp(current, softened, t);
        }

        private static Quaternion FilterRotation(Quaternion current, Quaternion raw, float t, float deadzoneDeg)
        {
            float ang = Quaternion.Angle(current, raw);
            if (ang < deadzoneDeg)
                return current;

            // Soften: move only the excess past deadzone so tiny flicks are ignored.
            float excess = ang - deadzoneDeg * 0.55f;
            float step = Mathf.Clamp01(excess / Mathf.Max(ang, 0.001f));
            Quaternion toward = Quaternion.Slerp(current, raw, step);
            return Quaternion.Slerp(current, toward, t);
        }

        private static Vector3 TransformPointStable(Vector3 pos, Quaternion rot, Vector3 local)
        {
            return pos + rot * local;
        }

        private void ApplyStableHeadFollow(
            Transform camera,
            Vector3 localOffset,
            float smooth,
            bool lockRoll)
        {
            Vector3 targetPos = TransformPointStable(filteredHeadPos, filteredHeadRot, localOffset);
            Quaternion targetRot = filteredHeadRot;

            if (lockRoll)
            {
                Vector3 forward = filteredHeadRot * Vector3.forward;
                if (forward.sqrMagnitude < 0.0001f)
                    forward = Vector3.forward;
                targetRot = Quaternion.LookRotation(forward, Vector3.up);
            }

            if (AngleClamping)
            {
                float angle = Quaternion.Angle(camera.rotation, targetRot);
                if (angle > maxAngle)
                    camera.rotation = Quaternion.RotateTowards(targetRot, camera.rotation, maxAngle);
            }

            if (!SmoothingEnabled)
            {
                camera.position = targetPos;
                filteredLookRot = targetRot;
                camera.rotation = targetRot;
                return;
            }

            // Curve favors ultra-low values: 0.001 feels soft/cinematic, 1.0 still snappy.
            float norm = Mathf.InverseLerp(MIN_SMOOTHING, MAX_SMOOTHING, Mathf.Max(MIN_SMOOTHING, smooth));
            float shaped = Mathf.Pow(norm, 0.55f);
            float followRate = Mathf.Lerp(0.85f, 28f, shaped);
            float t = StableFollowT(followRate);

            camera.position = Vector3.Lerp(camera.position, targetPos, t);
            filteredLookRot = Quaternion.Slerp(filteredLookRot, targetRot, t);
            camera.rotation = filteredLookRot;
        }

        private void ApplySmoothedLook(Transform camera, Quaternion targetRot)
        {
            if (!SmoothingEnabled)
            {
                filteredLookRot = targetRot;
                camera.rotation = targetRot;
                return;
            }

            float t = StableFollowT(LookFilterHz);
            filteredLookRot = Quaternion.Slerp(filteredLookRot, targetRot, t);
            camera.rotation = filteredLookRot;
        }

        private static float StableFollowT(float followRate)
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return 1f;
            // Clamp dt so hitch frames can't explode smoothing / SmoothDamp velocity.
            dt = Mathf.Min(dt, 0.05f);
            return 1f - Mathf.Exp(-Mathf.Max(0.01f, followRate) * dt);
        }

        private void ResetPoseFilters()
        {
            poseFilterReady = false;
            velocity = Vector3.zero;
        }
    }
}
