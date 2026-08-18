using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CameraMod.Camera.Networking;
using CameraMod.Extensions.GUI;
using GorillaLocomotion;
using GorillaNetworking;
using Photon.Pun;
using TUPshaders.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.Serialization;
using Debug = UnityEngine.Debug;

#pragma warning disable CS0618
namespace CameraMod.Camera.Comps {
    internal class UI : MonoBehaviour {
        private bool debugMode = true;
        private bool toShowAngleClampingDebugGUI = false;
        
        private bool controllerfreecam;
        private bool controloffset;
        private GameObject followobject;
        public bool freecam;
        private float freecamsens = 1f;
        private float freecamspeed = 0.1f;
        private float posY;

        private float rotX;
        private float rotY;
        private bool speclookat;
        private Vector3 specoffset = new Vector3(0.3f, 0.1f, -1.5f);
        private bool spectating;
        private bool specui;
        private bool uiopen;
        private bool tabTipDismissed;
        private Vector3 velocity = Vector3.zero;

        private Transform tabletTransform => CameraController.Instance != null
            ? CameraController.Instance.cameraTabletT
            : null;
        private Transform lensTransform => CameraController.Instance != null
            ? CameraController.Instance.LensT
            : null;

        public static UI Instance;
        
        private void Start() {
            Instance = this;
            // Open on desktop; also open when Tab tip is useful in VR+PC mixed.
            if (!IsHeadsetActive())
                uiopen = true;
        }

        private static bool IsHeadsetActive()
        {
            try
            {
                return UnityEngine.XR.XRSettings.isDeviceActive;
            }
            catch
            {
                return false;
            }
        }

        private void LateUpdate() {
            // Gorilla Tag Input System often leaves Keyboard.current null — use Win32.
            RawKeyboard.Tick();
            if (RawKeyboard.GetKeyDown(KeyCode.Tab))
            {
                uiopen = !uiopen;
                if (uiopen)
                    tabTipDismissed = true;
            }

            Spec();
            Freecam();
            // FreeCam always drives the whole PC monitor; other modes need Drive toggle.
            var camCtrl = CameraController.Instance;
            if (camCtrl != null && camCtrl.ShouldDrivePcMonitor())
                camCtrl.SyncPcCameraFromLens();
            if (PhotonNetworkController.Instance != null)
                PhotonNetworkController.Instance.disableAFKKick = spectating || freecam;
        }

        private void WaterMark() {
            float width = 200;
            float height = 50;

            float x = Screen.width - width - 10;
            float y = Screen.height - height - 10;

            GUIStyle style = new GUIStyle();
            style.fontSize = 20;
            style.normal.textColor = new Color(1, 1, 1, 0.1f);

            Rect labelRect = new Rect(x, y, width, height);
            GUI.Label(labelRect, "TUP Camera Mod", style);
        }

        public void SpecMode() {
            CameraController.Instance.cameraMode = CameraMode.None;
        }

        public string roomToJoin = "";

        public bool watermarkEnabled = false;

        private Rect mainWindowRect = new Rect(30, 50, 220, 0);
        private Rect specWindowRect = new Rect(270, 50, 300, 0);

        private void OnGUI() {
            if (toShowAngleClampingDebugGUI) {
                var cameraController = CameraController.Instance;
                CameraClampVisualizer.OnGUI(cameraController.thirdPersonCamera, cameraController.cameraFollowerT, CameraController.MaxAngle);
            }
            
            if (watermarkEnabled)
                WaterMark();

            if (!uiopen && !tabTipDismissed)
            {
                GUIStyle tipStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                tipStyle.normal.textColor = Color.white;
                GUI.Box(new Rect(Screen.width * 0.5f - 190f, Screen.height - 54f, 380f, 36f),
                    "Press Tab — TUP Camera (no headset needed)", tipStyle);
            }

            if (!uiopen)
                return;
            
            GUI.backgroundColor = new Color(0.12f, 0.10f, 0.16f, 0.95f);
            GUI.contentColor = Color.white;
            mainWindowRect = GUILayout.Window(1000, mainWindowRect, DrawMainWindow, "TUP Camera");

            if (PhotonNetwork.InRoom && specui)
                specWindowRect = GUILayout.Window(1001, specWindowRect, DrawSpectatorWindow, "Spectate");
            else {
                specui = false;
                followobject = null;
            }
        }

        public static GUIVector3 fpOffsetGUI = new GUIVector3();
        public static string clampAngleString;
        private void DrawMainWindow(int id) {
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };

            GUILayout.Label("TUP Camera", titleStyle);
            GUILayout.Label(IsHeadsetActive()
                ? "Tab toggles this window · VR: grip tablet"
                : "Desktop mode — Tab toggles · no headset needed");
            GUILayout.Space(8);

            if (GUILayout.Button(freecam ? "Stop FreeCam" : "FreeCam")) {
                if (!freecam) {
                    if (spectating) {
                        spectating = false;
                        followobject = null;
                    }

                    CameraController.Instance?.EnableFreeCam();
                    freecam = true;
                } else {
                    freecam = false;
                    CameraController.Instance?.BringTabletToPlayer(exitModes: true);
                }
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("3rd Person")) {
                freecam = false;
                spectating = false;
                followobject = null;
                CameraController.Instance?.EnableTPV();
            }
            if (GUILayout.Button("Follow")) {
                freecam = false;
                spectating = false;
                followobject = null;
                CameraController.Instance?.EnableFollow();
            }
            if (GUILayout.Button("POV")) {
                freecam = false;
                spectating = false;
                followobject = null;
                CameraController.Instance?.EnableFPV();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Smoothing / GUI");
            var ctrl = CameraController.Instance;
            if (ctrl != null)
            {
                bool keepGui = GUILayout.Toggle(ctrl.KeepGuiInActiveModes, "Keep GUI in POV / 3RD");
                if (keepGui != ctrl.KeepGuiInActiveModes)
                {
                    ctrl.KeepGuiInActiveModes = keepGui;
                    PlayerPrefs.SetInt("TUP_CameraKeepGui", keepGui ? 1 : 0);
                }

                bool noSmooth = GUILayout.Toggle(!ctrl.SmoothingEnabled, "Disable Smoothing (snap)");
                if (noSmooth == ctrl.SmoothingEnabled)
                    ctrl.SetSmoothingEnabled(!noSmooth);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Smooth", GUILayout.Width(50));
                if (GUILayout.Button("-", GUILayout.Width(28)))
                    ctrl.ChangeSmoothing(-0.01f);
                GUILayout.Label(ctrl.SmoothingEnabled ? ctrl.smoothing.ToString(ctrl.smoothing < 0.01f ? "0.000" : "0.##") : "OFF", GUILayout.Width(56));
                if (GUILayout.Button("+", GUILayout.Width(28)))
                    ctrl.ChangeSmoothing(0.01f);
                GUILayout.EndHorizontal();

                GUILayout.Label(ctrl.KeepGuiInActiveModes
                    ? "Tablet stays put — only the lens moves"
                    : "Tablet can hide while POV/3RD is active");
            }

            GUILayout.Space(4);
            GUILayout.Label("Summon Bind: " + CameraMod.Binds.GetActivateBindLabel());
            if (GUILayout.Button("Cycle Summon Bind"))
                CameraMod.Binds.CycleActivateBind();

            GUILayout.Space(6);
            GUILayout.Label("PC monitor");
            if (ctrl != null)
            {
                bool freeDriving = ctrl.cameraMode == CameraMode.FreeCam;
                bool drive = GUILayout.Toggle(ctrl.DrivePcMonitor || freeDriving,
                    freeDriving
                        ? "FreeCam owns PC monitor (always on)"
                        : "Drive PC monitor from POV/3RD (OFF = normal GT view)");
                if (!freeDriving && drive != ctrl.DrivePcMonitor)
                {
                    ctrl.DrivePcMonitor = drive;
                    PlayerPrefs.SetInt("TUP_DrivePcMonitor", drive ? 1 : 0);
                    if (!drive)
                        ctrl.RestoreGtPcCamera();
                }

                if (GUILayout.Button("Fix Zoomed PC View Now"))
                    ctrl.ForceFixZoomedPcView();
            }

            GUILayout.Space(6);
            GUILayout.Label("View framing");
            if (ctrl != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("FOV", GUILayout.Width(36));
                if (GUILayout.Button("-", GUILayout.Width(28)))
                    ctrl.ChangeFov(-5f);
                GUILayout.Label(ctrl.GetFovDisplay() + "  (tablet preview)", GUILayout.Width(140));
                if (GUILayout.Button("+", GUILayout.Width(28)))
                    ctrl.ChangeFov(5f);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Reset Zoom / Pull Camera Out"))
                {
                    ctrl.ResetViewFraming();
                    ctrl.SnapLensToTabletOutward();
                    ctrl.RestoreGtPcCamera();
                }
            }

            GUILayout.Space(6);
            GUILayout.Label(IsHeadsetActive()
                ? "VR: Grip tablet rails · poke buttons"
                : "FreeCam moves lens only — tablet stays put");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spectator")) {
                if (!freecam && PhotonNetwork.InRoom)
                    specui = !specui;
                SpecMode();
            }

            if (GUILayout.Button("StopIfPlaying", GUILayout.Width(120))) {
                if (spectating) {
                    followobject = null;
                    spectating = false;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            controllerfreecam = GUILayout.Toggle(controllerfreecam, "Controller Freecam");
            controloffset = GUILayout.Toggle(controloffset, "Control Offset with WASD");
            speclookat = GUILayout.Toggle(speclookat, "Spectator Stare");

            GUILayout.Space(5);
            GUILayout.Label("Spectator View Offset");
            GUILayout.BeginHorizontal();
            specoffset.x = GUILayout.HorizontalSlider(specoffset.x, -3, 3);
            specoffset.y = GUILayout.HorizontalSlider(specoffset.y, -3, 3);
            specoffset.z = GUILayout.HorizontalSlider(specoffset.z, -3, 3);
            GUILayout.EndHorizontal();
            
            GUILayout.Space(5);
            GUILayout.Label("First Person View Offset");
            var lastFPOffset = CameraController.FirstPersonOffset;
            fpOffsetGUI.Draw(ref CameraController.FirstPersonOffset);
            if (lastFPOffset != CameraController.FirstPersonOffset) {
                PlayerPrefs.SetFloat("FPOffset_x", CameraController.FirstPersonOffset.x);
                PlayerPrefs.SetFloat("FPOffset_y", CameraController.FirstPersonOffset.y);
                PlayerPrefs.SetFloat("FPOffset_z", CameraController.FirstPersonOffset.z);
            }
            
            GUILayout.Space(5);
            GUILayout.Label("Freecam Speed");
            freecamspeed = GUILayout.HorizontalSlider(freecamspeed, 0.01f, 0.4f);

            GUILayout.Space(5);
            GUILayout.Label("Freecam Sens");
            freecamsens = GUILayout.HorizontalSlider(freecamsens, 0.01f, 2f);

            GUILayout.Space(5);

            if (GUILayout.Button("Copy UserID")) {
                GUIUtility.systemCopyBuffer = PlayFabAuthenticator.instance.GetPlayFabPlayerId();
            }
            
            GUILayout.Space(5);

            if (PhotonNetwork.InRoom) {
                if (GUILayout.Button($"Leave {PhotonNetwork.CurrentRoom.Name}", GUILayout.Height(45)))
                    PhotonNetwork.Disconnect();
            } else {
                roomToJoin = GUILayout.TextField(roomToJoin.Replace(@"\", ""), GUILayout.Height(20));
                if (GUILayout.Button("Join Room", GUILayout.Height(20)))
                    PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(roomToJoin, JoinType.Solo);
            }

            var toClamp = GUILayout.Toggle(CameraController.AngleClamping, "Angle Clamping");
            if (toClamp != CameraController.AngleClamping) {
                CameraController.AngleClamping = toClamp;
            }
            if (toClamp) {
                if (clampAngleString == null) {
                    clampAngleString = CameraController.MaxAngle.ToString();
                }
                clampAngleString = GUILayout.TextField(clampAngleString, GUILayout.Height(20));
                if (GUILayout.Button("Set Max Angle")) {
                    if (float.TryParse(clampAngleString, out var newClampAngle)) {
                        CameraController.MaxAngle = newClampAngle;
                    } else {
                        clampAngleString = CameraController.MaxAngle.ToString();
                    }
                }
                if (debugMode) {
                    toShowAngleClampingDebugGUI = GUILayout.Toggle(toShowAngleClampingDebugGUI, "Debug Angle Clamping GUI");
                }
            }

            if (GUILayout.Toggle(ReplicationHandler.HideOtherTablets, "Hide Other Tablets") !=
                ReplicationHandler.HideOtherTablets) {
                ReplicationHandler.HideOtherTablets = !ReplicationHandler.HideOtherTablets;
            }

            GUILayout.Space(10);
            
            if (GUILayout.Button("Join Discord")) Process.Start("https://discord.gg/8qaPhVjpsG");
            
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawSpectatorWindow(int id) {
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };

            GUILayout.Label("Spectate", titleStyle);
            GUILayout.Space(5);
            
            foreach (var player in PhotonNetwork.PlayerListOthers) {
                VRRig vrrig = null;
                foreach (VRRig rig in VRRigCache.ActiveRigs)
                {
                    if (rig == null || rig.isLocal)
                        continue;
                    try
                    {
                        if (rig.Creator != null && rig.Creator.ActorNumber == player.ActorNumber)
                        {
                            vrrig = rig;
                            break;
                        }
                    }
                    catch { }
                }

                if (vrrig == null)
                    continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label(player.NickName);
                if (GUILayout.Button("Spectate", GUILayout.Width(80))) {
                    followobject = vrrig.gameObject;
                    spectating = true;
                    SpecMode();
                    if (CameraController.Instance.isFaceCamera)
                        CameraController.Instance.Flip();
                }
                GUILayout.EndHorizontal();
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }


        private void Freecam() {
            Transform lens = lensTransform;
            if (lens == null)
                return;

            // Prefer CameraController mode when FreeCam; keep legacy freecam flag in sync.
            bool active = freecam
                          || (CameraController.Instance != null
                              && CameraController.Instance.cameraMode == CameraMode.FreeCam);
            if (!active)
                return;

            freecam = true;

            if (!controllerfreecam) {
                // Win32 keys — Keyboard.current is often null in GT.
                if (RawKeyboard.GetKey(KeyCode.W))
                    lens.position += lens.forward * freecamspeed;
                if (RawKeyboard.GetKey(KeyCode.S))
                    lens.position -= lens.forward * freecamspeed;
                if (RawKeyboard.GetKey(KeyCode.A))
                    lens.position -= lens.right * freecamspeed;
                if (RawKeyboard.GetKey(KeyCode.D))
                    lens.position += lens.right * freecamspeed;
                if (RawKeyboard.GetKey(KeyCode.Q))
                    lens.position -= lens.up * freecamspeed;
                if (RawKeyboard.GetKey(KeyCode.E))
                    lens.position += lens.up * freecamspeed;
                if (RawKeyboard.GetKey(KeyCode.LeftArrow))
                    lens.Rotate(0f, -freecamsens, 0f, Space.World);
                if (RawKeyboard.GetKey(KeyCode.RightArrow))
                    lens.Rotate(0f, freecamsens, 0f, Space.World);
                if (RawKeyboard.GetKey(KeyCode.UpArrow))
                    lens.Rotate(-freecamsens, 0f, 0f, Space.Self);
                if (RawKeyboard.GetKey(KeyCode.DownArrow))
                    lens.Rotate(freecamsens, 0f, 0f, Space.Self);
            }

            if (controllerfreecam && InputManager.instance != null) {
                var x = InputManager.instance.GPLeftStick.x;
                var y = InputManager.instance.GPLeftStick.y;
                rotX += InputManager.instance.GPRightStick.x * freecamsens;
                rotY += InputManager.instance.GPRightStick.y * freecamsens;
                var movementdir = new Vector3(x, posY, y);
                lens.Translate(movementdir * freecamspeed, Space.Self);
                rotY = Mathf.Clamp(rotY, -90f, 90f);
                lens.rotation = Quaternion.Euler(rotY, rotX, 0);
                if (Gamepad.current != null && Gamepad.current.rightShoulder.isPressed)
                    posY = 3f * freecamspeed;
                else if (Gamepad.current != null && Gamepad.current.leftShoulder.isPressed)
                    posY = -3f * freecamspeed;
                else
                    posY = 0;
            }
        }

        private void Spec() {
            Transform lens = lensTransform;
            if (lens == null)
                return;

            if (followobject != null) {
                var targetPosition = followobject.transform.TransformPoint(specoffset);
                lens.position =
                        Vector3.SmoothDamp(lens.position, targetPosition, ref velocity, 0.2f);
                if (speclookat) {
                    var targetRotation =
                            Quaternion.LookRotation(followobject.transform.position - lens.position);
                    lens.rotation = Quaternion.Lerp(lens.rotation, targetRotation, 0.2f);
                } else {
                    lens.rotation =
                            Quaternion.Lerp(lens.rotation, followobject.transform.rotation, 0.2f);
                }

                if (controloffset) {
                    if (RawKeyboard.GetKey(KeyCode.W))
                    {
                        if (specoffset.z >= 3.01) specoffset.z = 3;
                        specoffset.z += 0.02f;
                    }

                    if (RawKeyboard.GetKey(KeyCode.A))
                    {
                        if (specoffset.x <= -3.01) specoffset.x = -3;
                        specoffset.x -= 0.02f;
                    }

                    if (RawKeyboard.GetKey(KeyCode.S))
                    {
                        if (specoffset.z <= -3.01) specoffset.z = -3;
                        specoffset.z -= 0.02f;
                    }

                    if (RawKeyboard.GetKey(KeyCode.D))
                    {
                        if (specoffset.x >= 3.01) specoffset.x = 3;
                        specoffset.x += 0.02f;
                    }

                    if (RawKeyboard.GetKey(KeyCode.Q))
                    {
                        if (specoffset.y <= -3.01) specoffset.y = -3;
                        specoffset.y -= 0.02f;
                    }

                    if (RawKeyboard.GetKey(KeyCode.E))
                    {
                        if (specoffset.y >= 3.01) specoffset.y = 3;
                        specoffset.y += 0.02f;
                    }
                }
            } else if (spectating) {
                spectating = false;
            }
        }
    }
}
