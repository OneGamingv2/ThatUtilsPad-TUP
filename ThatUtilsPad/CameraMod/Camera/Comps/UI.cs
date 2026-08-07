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
        private Vector3 velocity = Vector3.zero;

        private Transform tabletTransform => CameraController.Instance.cameraTabletT;

        public static UI Instance;
        
        private void Start() {
            Instance = this;
        }

        private void LateUpdate() {
            Spec();
            Freecam();
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

        private Rect mainWindowRect = new Rect(30, 50, 150, 0);
        private Rect specWindowRect = new Rect(250, 50, 300, 0);

        private void OnGUI() {
            if (toShowAngleClampingDebugGUI) {
                var cameraController = CameraController.Instance;
                CameraClampVisualizer.OnGUI(cameraController.thirdPersonCamera, cameraController.cameraFollowerT, CameraController.MaxAngle);
            }
            
            if (watermarkEnabled)
                WaterMark();

            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                uiopen = !uiopen;

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
            GUILayout.Space(10);

            if (GUILayout.Button(freecam ? "FirstPersonView" : "FreeCam")) {
                if (!freecam) {
                    if (spectating) {
                        spectating = false;
                        followobject = null;
                    }

                    if (!CameraController.Instance.isFaceCamera) {
                        CameraController.Instance.isFaceCamera = true;
                        CameraController.Instance.thirdPersonCameraT.Rotate(0, 180, 0);
                        CameraController.Instance.tabletCameraT.Rotate(0, 180, 0);
                    }

                    SpecMode();
                    freecam = true;
                } else {
                    freecam = false;
                    CameraController.Instance.EnableFPV();
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
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spectator")) {
                if (!freecam && PhotonNetwork.InRoom)
                    specui = !specui;
                SpecMode();
            }

            if (GUILayout.Button("StopIfPlaying", GUILayout.Width(120))) {
                if (spectating) {
                    followobject = null;
                    tabletTransform.position = GTPlayer.Instance.headCollider.transform.position +
                                               GTPlayer.Instance.headCollider.transform.forward;
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
            if (freecam && !controllerfreecam) {
                if (Keyboard.current.wKey.isPressed)
                    tabletTransform.position -= tabletTransform.forward * +freecamspeed;
                if (Keyboard.current.aKey.isPressed) tabletTransform.position += tabletTransform.right * +freecamspeed;
                if (Keyboard.current.sKey.isPressed)
                    tabletTransform.position += tabletTransform.forward * +freecamspeed;
                if (Keyboard.current.dKey.isPressed) tabletTransform.position -= tabletTransform.right * +freecamspeed;
                if (Keyboard.current.qKey.isPressed) tabletTransform.position -= tabletTransform.up * +freecamspeed;
                if (Keyboard.current.eKey.isPressed) tabletTransform.position += tabletTransform.up * +freecamspeed;
                if (Keyboard.current.leftArrowKey.isPressed)
                    tabletTransform.eulerAngles += new Vector3(0f, -freecamsens, 0f);
                if (Keyboard.current.rightArrowKey.isPressed)
                    tabletTransform.eulerAngles += new Vector3(0f, freecamsens, 0f);
                if (Keyboard.current.upArrowKey.isPressed)
                    tabletTransform.eulerAngles += new Vector3(freecamsens, 0f, 0f);
                if (Keyboard.current.downArrowKey.isPressed)
                    tabletTransform.eulerAngles += new Vector3(-freecamsens, 0f, 0f);
            }

            if (freecam && controllerfreecam) {
                var x = InputManager.instance.GPLeftStick.x;
                var y = InputManager.instance.GPLeftStick.y;
                rotX += InputManager.instance.GPRightStick.x * freecamsens;
                rotY += InputManager.instance.GPRightStick.y * freecamsens;
                var movementdir = new Vector3(-x, posY, -y);
                tabletTransform.Translate(movementdir * freecamspeed);
                rotY = Mathf.Clamp(rotY, -90f, 90f);
                tabletTransform.rotation = Quaternion.Euler(rotY, rotX, 0);
                if (Gamepad.current.rightShoulder.isPressed)
                    posY = 3f * +freecamspeed;
                else if (Gamepad.current.leftShoulder.isPressed)
                    posY = -3f * +freecamspeed;
                else
                    posY = 0;
            }
        }

        private void Spec() {
            if (followobject != null) {
                var targetPosition = followobject.transform.TransformPoint(specoffset);
                tabletTransform.position =
                        Vector3.SmoothDamp(tabletTransform.position, targetPosition, ref velocity, 0.2f);
                if (speclookat) {
                    var targetRotation =
                            Quaternion.LookRotation(followobject.transform.position - tabletTransform.position);
                    tabletTransform.rotation = Quaternion.Lerp(tabletTransform.rotation, targetRotation, 0.2f);
                } else {
                    tabletTransform.rotation =
                            Quaternion.Lerp(tabletTransform.rotation, followobject.transform.rotation, 0.2f);
                }

                if (controloffset) {
                    if (Keyboard.current.wKey.isPressed) // forward
                    {
                        if (specoffset.z >= 3.01) specoffset.z = 3;
                        specoffset.z += 0.02f;
                    }

                    if (Keyboard.current.aKey.isPressed) // left
                    {
                        if (specoffset.x <= -3.01) specoffset.x = -3;
                        specoffset.x -= 0.02f;
                    }

                    if (Keyboard.current.sKey.isPressed) // back
                    {
                        if (specoffset.z <= -3.01) specoffset.z = -3;
                        specoffset.z -= 0.02f;
                    }

                    if (Keyboard.current.dKey.isPressed) // right
                    {
                        if (specoffset.x >= 3.01) specoffset.x = 3;
                        specoffset.x += 0.02f;
                    }

                    if (Keyboard.current.qKey.isPressed) // up 
                    {
                        if (specoffset.y <= -3.01) specoffset.y = -3;
                        specoffset.y -= 0.02f;
                    }

                    if (Keyboard.current.eKey.isPressed) // down
                    {
                        if (specoffset.y >= 3.01) specoffset.y = 3;
                        specoffset.y += 0.02f;
                    }
                }
            } else {
                if (spectating) {
                    tabletTransform.position = GTPlayer.Instance.headCollider.transform.position +
                                               GTPlayer.Instance.headCollider.transform.forward;
                    spectating = false;
                }
            }
        }
    }
}
