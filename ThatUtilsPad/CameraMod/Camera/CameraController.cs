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

        public MainPage mainPage;
        public MiscPage miscPage;

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
        public TpvModes tpvMode = TpvModes.Back;

        private bool init;
        private bool initInProgress;
        public bool IsReady => init;
        private Vector3 velocity = Vector3.zero;
        private Appearance appearance;
        private string skinPrefKey = "PokruksSkin";
        private float lastPageChangedTime;
        private readonly float pageChangeButtonsTimeout = 0.2f;
        public HeadCosmeticsHider HeadCosmeticsHider;
        public bool isCosmeticsHiderInited;

        public bool ButtonsTimeouted => Time.time - lastPageChangedTime < pageChangeButtonsTimeout;

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

            float near = val;
            if (near < 0.01f) near = 1f;
            if (near > 1.0f) near = 0.01f;

            tabletCamera.nearClipPlane = near;
            if (thirdPersonCamera != null)
                thirdPersonCamera.nearClipPlane = near;
            if (mainPage?.NearClipText != null)
                mainPage.NearClipText.text = near.ToString("#.##");
        }

        public void ChangeNearClip(float diff)
        {
            if (tabletCamera == null)
                return;
            SetNearClip(tabletCamera.nearClipPlane + diff);
            PlayerPrefs.SetFloat("CameraNearClip", tabletCamera.nearClipPlane);
        }

        private const float MIN_SMOOTHING = 0.01f;
        private const float MAX_SMOOTHING = 1f;

        public void SetSmoothing(float val)
        {
            smoothing = Mathf.Clamp(val, MIN_SMOOTHING, MAX_SMOOTHING);
            if (mainPage?.SmoothText != null)
                mainPage.SmoothText.text = smoothing.ToString("#.##");
        }

        public void ChangeSmoothing(float change)
        {
            SetSmoothing(smoothing + change);
            PlayerPrefs.SetFloat("CameraSmoothing", smoothing);
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
            if (thirdPersonCamera != null)
                thirdPersonCamera.fieldOfView = newFov;
            if (mainPage?.FOVText != null)
                mainPage.FOVText.text = newFov.ToString("#.##");
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
            appearance.isOwner = StartPatch.owners.Contains(StartPatch.UserID);
        }

        private void SetSkin(string skinName)
        {
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
            cameraTabletT = ProceduralGui.BuildCameraTablet().transform;
            cameraTabletT.localScale = Vector3.one;
            UnityEngine.Object.DontDestroyOnLoad(cameraTabletT.gameObject);
            UnityEngine.Object.DontDestroyOnLoad(colorScreenGo);

            SetSkin(PlayerPrefs.GetString(skinPrefKey, "Default"));

            thirdPersonCameraT = shoulder;
            DisableCinemachine(shoulder);
            tpvBodyFollowerT = tagger.bodyCollider != null ? tagger.bodyCollider.transform : tagger.transform;
            thirdPersonCamera = thirdPersonCameraT.GetComponent<UnityEngine.Camera>();
            cameraFollowerT = follower;

            tabletCameraT = cameraTabletT.Find("Camera");
            if (tabletCameraT == null)
                throw new Exception("Procedural tablet missing Camera child");
            tabletCamera = tabletCameraT.GetComponent<UnityEngine.Camera>();

            Transform leftGrab = cameraTabletT.Find("LeftGrabCol");
            Transform rightGrab = cameraTabletT.Find("RightGrabCol");
            if (leftGrab != null) leftGrab.gameObject.AddComponent<LeftGrabTrigger>();
            if (rightGrab != null) rightGrab.gameObject.AddComponent<RightGrabTrigger>();

            Transform mainPageT = cameraTabletT.Find("MainPage");
            Transform miscPageT = cameraTabletT.Find("MiscPage");
            if (mainPageT == null || miscPageT == null)
                throw new Exception("Procedural tablet missing MainPage/MiscPage");

            mainPage = new MainPage(mainPageT);
            miscPage = new MiscPage(miscPageT);

            RegisterButtons();

            if (thirdPersonCameraT != null && tabletCamera != null)
            {
                thirdPersonCameraT.SetParent(cameraTabletT, true);
                thirdPersonCameraT.localPosition = tabletCamera.transform.localPosition;
                thirdPersonCameraT.localRotation = tabletCamera.transform.localRotation;
            }

            SetupColorScreen();

            if (miscPage?.GO != null)
                miscPage.GO.SetActive(false);
            if (thirdPersonCamera != null)
                thirdPersonCamera.nearClipPlane = 0.1f;
            if (tabletCamera != null)
                tabletCamera.nearClipPlane = 0.1f;

            init = true;

            SetFov(PlayerPrefs.GetInt("CameraFov", 100));
            SetSmoothing(PlayerPrefs.GetFloat("CameraSmoothing", 0.07f));
            SetNearClip(PlayerPrefs.GetFloat("CameraNearClip", 0.05f));
            angleClamping = PlayerPrefs.GetInt("AngleClamping", 1) == 1;
            maxAngle = PlayerPrefs.GetFloat("AngleClampingValue", 30f);

            Binds.Init();
            BringTabletToPlayer();
            Debug.Log("[TUP Camera] Ready. Buttons=" + buttons.Count);
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

        private static void DisableCinemachine(Transform shoulder)
        {
            if (shoulder == null)
                return;

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

            if (vcam == null)
                return;

            var cm = vcam.GetComponent<CinemachineVirtualCamera>();
            if (cm != null)
                cm.enabled = false;
        }

        public void Flip()
        {
            if (thirdPersonCameraT == null || tabletCameraT == null)
                return;
            isFaceCamera = !isFaceCamera;
            thirdPersonCameraT.Rotate(0f, 180f, 0f);
            tabletCameraT.Rotate(0f, 180f, 0f);
        }

        public void InitCosmeticsHider()
        {
            if (isCosmeticsHiderInited || !init || thirdPersonCameraT == null || cameraTabletT == null)
                return;

            isCosmeticsHiderInited = true;
            HeadCosmeticsHider = thirdPersonCameraT.gameObject.GetComponent<HeadCosmeticsHider>()
                                 ?? thirdPersonCameraT.gameObject.AddComponent<HeadCosmeticsHider>();

            Transform hideBtn = cameraTabletT.Find("MainPage/HideHeadCosmetics");
            if (hideBtn == null)
                return;

            ToggleButton toggle = hideBtn.gameObject.GetComponent<ToggleButton>()
                                  ?? hideBtn.gameObject.AddComponent<ToggleButton>();
            toggle.InitToggleButton(b => HeadCosmeticsHider.enabled = b, () => HeadCosmeticsHider.enabled);
            if (!buttons.Contains(toggle))
                buttons.Add(toggle);
        }

        public void EnableFPV()
        {
            if (!init)
                return;
            if (isFaceCamera)
                Flip();
            cameraMode = CameraMode.FirstPersonView;
            if (UI.Instance != null)
                UI.Instance.freecam = false;
        }

        public void EnableTPV()
        {
            if (!init)
                return;
            if (tpvMode == TpvModes.Back)
            {
                if (isFaceCamera) Flip();
            }
            else if (tpvMode == TpvModes.Front)
            {
                if (!isFaceCamera) Flip();
            }

            cameraMode = CameraMode.ThirdPerson;
        }

        public void EnableFollow()
        {
            if (!init)
                return;
            cameraMode = cameraMode == CameraMode.FollowPlayer ? CameraMode.None : CameraMode.FollowPlayer;
            if (cameraMode == CameraMode.None)
                BringTabletToPlayer();
        }

        public void BringTabletToPlayer()
        {
            if (!init || cameraTabletT == null)
                return;

            cameraMode = CameraMode.None;
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

            Transform head = null;
            if (GTPlayer.Instance != null && GTPlayer.Instance.headCollider != null)
                head = GTPlayer.Instance.headCollider.transform;
            else if (GorillaTagger.Instance != null && GorillaTagger.Instance.mainCamera != null)
                head = GorillaTagger.Instance.mainCamera.transform;

            if (head == null)
                return;

            cameraTabletT.position = head.position + head.forward * 0.65f;
            cameraTabletT.LookAt(head.position);
            cameraTabletT.Rotate(0f, -180f, 0f);
        }

        public void RegisterButtons()
        {
            AddTabletButton("MiscPage/BackButton", () =>
            {
                if (mainPage?.GO != null) mainPage.GO.SetActive(true);
                if (miscPage?.GO != null) miscPage.GO.SetActive(false);
                lastPageChangedTime = Time.time;
            });

            AddTabletButton("MainPage/MiscButton", () =>
            {
                if (mainPage?.GO != null) mainPage.GO.SetActive(false);
                if (miscPage?.GO != null) miscPage.GO.SetActive(true);
                lastPageChangedTime = Time.time;
            });

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
                    .InitToggleButton(b => RollLock = b, () => RollLock));
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
                    .InitToggleButton(b => colorScreenGo.SetActive(b), () => colorScreenGo.activeSelf));
            }

            Transform skins = cameraTabletT.Find("MiscPage/Skins");
            if (skins != null)
            {
                foreach (Transform child in skins)
                    AddTabletButton("MiscPage/Skins/" + child.name, () => SetSkin(child.name));
            }
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
            Transform t = cameraTabletT != null ? cameraTabletT.Find(relativeButtonPath) : null;
            if (t == null)
            {
                Debug.LogWarning("[TUP Camera] Missing button: " + relativeButtonPath);
                return;
            }
            buttons.Add(Button(t, onClick));
        }

        public void AddHoldableTabletButton(string buttonPath, Action onClick)
        {
            Transform t = cameraTabletT != null ? cameraTabletT.Find(buttonPath) : null;
            if (t == null)
            {
                Debug.LogWarning("[TUP Camera] Missing hold button: " + buttonPath);
                return;
            }
            buttons.Add(t.gameObject.AddComponent<HoldableButton>().OnClick(onClick));
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

            SetChild("FakeCamera", visible);
            SetChild("FacePlate", visible);
            SetChild("AccentRail", visible);
            SetChild("LeftGrabCol", visible);
            SetChild("RightGrabCol", visible);

            if (tabletCamera != null)
                tabletCamera.enabled = true;
        }

        private float lastPokeInvoke;
        private BaseButton lastPokedButton;

        private void PollTabletButtonPokes()
        {
            if (cameraMode != CameraMode.None || cameraTabletT == null || ButtonsTimeouted)
                return;
            if (Time.time - lastPokeInvoke < 0.18f)
                return;

            bool pageOpen = (mainPage?.GO != null && mainPage.GO.activeInHierarchy)
                            || (miscPage?.GO != null && miscPage.GO.activeInHierarchy);
            if (!pageOpen)
                return;

            Transform left = GorillaTagger.Instance?.leftHandTransform;
            Transform right = GorillaTagger.Instance?.rightHandTransform;
            if (left == null && right == null)
                return;

            const float pokeRadius = 0.095f;
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

            if (cameraMode == CameraMode.FirstPersonView)
            {
                if (mainPage?.GO != null && mainPage.GO.activeSelf)
                {
                    SetTabletVisibility(false);
                    mainPage.GO.SetActive(false);
                }

                if (cameraFollowerT == null)
                    return;

                Transform camera = cameraTabletT;
                Transform follower = cameraFollowerT;
                camera.position = follower.TransformPoint(FirstPersonOffset);
                if (AngleClamping && Quaternion.Angle(camera.rotation, follower.rotation) > maxAngle)
                    camera.rotation = Quaternion.RotateTowards(follower.rotation, camera.rotation, maxAngle);

                Quaternion newRotation = camera.rotation.Lerped(follower.rotation, smoothing);
                if (RollLock)
                    newRotation = newRotation.eulerAngles.Scaled(new Vector3(1, 1, 0)).ToQuaternion();
                camera.rotation = newRotation;
            }

            if (BindEnabled && Binds.Tablet() && cameraTabletT.parent == null)
                BringTabletToPlayer();

            if (cameraMode == CameraMode.FollowPlayer && cameraFollowerT != null)
            {
                cameraTabletT.LookAt(2f * cameraTabletT.position - cameraFollowerT.position);
                if (!isFaceCamera)
                    Flip();

                float dist = Vector3.Distance(cameraFollowerT.position, cameraTabletT.position);
                if (dist > minDist)
                {
                    cameraTabletT.position = Vector3.Lerp(
                        cameraTabletT.position,
                        cameraFollowerT.position,
                        fpspeed);
                }
            }

            if (cameraMode == CameraMode.ThirdPerson)
            {
                if (mainPage?.GO != null && mainPage.GO.activeSelf)
                {
                    SetTabletVisibility(false);
                    mainPage.GO.SetActive(false);
                }

                if (cameraFollowerT == null || tpvBodyFollowerT == null)
                    return;

                Vector3 targetPosition;
                switch (tpvMode)
                {
                    case TpvModes.Back:
                        targetPosition = followheadrot
                            ? cameraFollowerT.TransformPoint(new Vector3(0.3f, 0.1f, -1.5f))
                            : tpvBodyFollowerT.TransformPoint(new Vector3(0.3f, 0.1f, -1.5f));
                        cameraTabletT.position = Vector3.SmoothDamp(cameraTabletT.position, targetPosition, ref velocity, 0.1f);
                        cameraTabletT.LookAt(cameraFollowerT.position);
                        break;
                    case TpvModes.Front:
                        targetPosition = followheadrot
                            ? cameraFollowerT.TransformPoint(new Vector3(0.1f, 0.3f, 2.5f))
                            : tpvBodyFollowerT.TransformPoint(new Vector3(0.1f, 0.3f, 2.5f));
                        cameraTabletT.position = Vector3.SmoothDamp(cameraTabletT.position, targetPosition, ref velocity, 0.1f);
                        cameraTabletT.LookAt(2f * cameraTabletT.position - cameraFollowerT.position);
                        break;
                }

                if (Binds.Tablet())
                {
                    cameraTabletT.parent = null;
                    BringTabletToPlayer();
                }
            }
        }
    }
}
