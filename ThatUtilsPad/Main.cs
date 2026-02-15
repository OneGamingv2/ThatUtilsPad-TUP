using BepInEx;
using GorillaLocomotion;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Schema;
using TMPro;
using UnityEngine;
using static ThrowableBug;

namespace ThatUtilsPad
{
    [BepInPlugin("that.utils.pad", "ThatUtilsPad", "1.0.0")]
    public class Main : BaseUnityPlugin
    {
        // Menu
        AssetBundle menuBundle;
        AssetBundle sakuraBundle;
        AssetBundle buttonBundle;

        GameObject btnPrefab;

        Vector3 menuGripPosition = new Vector3(0f, -0.17f, 0f);
        float menuGripRotaton = -30f;

        GameObject menuObj;
        List<GameObject> btnObjs = new List<GameObject>();
        Vector3 menuHandOffset = new Vector3(0f, 0f, 0f); //new Vector3(0.04f, 0f, 0f)

        // random Bools
        bool useSakuraTheme = true;
        bool isMenuOpened = false;
        bool alwaysShowMenu = true;

        // Theme colors thing
        Color32 mainColor = new Color32(17, 17, 27, 255); //171, 0, 63, 255
        Color32 borderColor = new Color32(12, 12, 22, 255);
        Color32 accentColor = new Color32(203, 166, 247, 255);
        Color32 buttonColor = new Color32(30, 30, 46, 255);
        Color32 buttonOutlineColor = new Color32(18, 18, 36, 255);

        // Blue colors
        //Color32 mainColor = new Color32(52, 129, 194, 255); //171, 0, 63, 255
        //Color32 accentColor = new Color32(25, 118, 194, 255);
        //Color32 buttonColor = new Color32(32, 113, 179, 255);

        // General
        public static Main Instance;
        void Awake() { Instance = this; }

        void Start()
        {
            Debug.Log("[TUP] ThatUtilsPad has Loaded");
            Mods.Init();
            LoadBundles();

            btnPrefab = buttonBundle.LoadAsset<GameObject>("assets/prefabs/tup-buttonmodel.prefab");
        }

        void Update()
        {
            bool shouldShow = ControllerInputPoller.instance.leftControllerSecondaryButton || alwaysShowMenu;

            if (!isMenuOpened && shouldShow)
            {
                isMenuOpened = true;
                OpenMenu();
                CreateButton(0.24f, "Disconnect");
                CreateButton(0.17f, "Join Random");
                CreateButton(0.10f, "Lobby Hop");

            }
            else if (isMenuOpened && !shouldShow)
            {
                CloseMenu();
            }
        }

        void CloseMenu()
        {
            isMenuOpened = false;
            Destroy(menuObj);
            DestroyButtons();
            menuObj = null;
        }

        void OpenMenu()
        {
            if (useSakuraTheme)
            {
                GameObject prefab = sakuraBundle.LoadAsset<GameObject>("assets/prefabs/tup-modelsmooth-sakura.prefab");
                menuObj = Instantiate(prefab);
            }
            else
            {
                GameObject prefab = menuBundle.LoadAsset<GameObject>("assets/prefabs/tup-modelsmooth.prefab");
                menuObj = Instantiate(prefab);
            }

            menuObj.transform.parent = GTPlayer.Instance.LeftHand.controllerTransform;
            menuObj.transform.localScale = Vector3.one * 0.625f; // new Vector3(0.015f, 0.3f, 0.45f);
            menuObj.transform.localPosition = Vector3.zero + menuHandOffset + menuGripPosition;
            menuObj.transform.localRotation = Quaternion.identity * Quaternion.Euler(270f, 180f, 0f);

            var rb = menuObj.GetComponent<Rigidbody>();
            if (rb != null)
                Destroy(rb);

            var collider = menuObj.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = menuObj.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                renderer.material.shader = Shader.Find("GorillaTag/UberShader");
                renderer.material.color = new Color32(171, 0, 63, 255);
            }

            // Load the colors and theme here (jelly)
            if (useSakuraTheme)
                AssignMenuThemeSakura();
            else
                AssignMenuTheme();
            
        }

        void AssignMenuTheme()
        {
            Transform Main = menuObj.transform.Find("Main");
            Main.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            Main.GetComponent<Renderer>().material.color = mainColor;

            Transform MainBorder = menuObj.transform.Find("MainBorder");
            MainBorder.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            MainBorder.GetComponent<Renderer>().material.color = borderColor;

            Transform GripPipe = menuObj.transform.Find("GripPipe");
            GripPipe.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            GripPipe.GetComponent<Renderer>().material.color = mainColor;

            Transform GripAccent = menuObj.transform.Find("GripAccent");
            GripAccent.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            GripAccent.GetComponent<Renderer>().material.color = accentColor;
        }

        void AssignMenuThemeSakura()
        {
            Transform Main = menuObj.transform.Find("Main");
            Main.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            Main.GetComponent<Renderer>().material.color = mainColor;

            Transform MainBorder = menuObj.transform.Find("MainBorder");
            MainBorder.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            MainBorder.GetComponent<Renderer>().material.color = borderColor;

            Transform GripPipe = menuObj.transform.Find("GripPipe");
            GripPipe.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            GripPipe.GetComponent<Renderer>().material.color = mainColor;

            Transform GripAccent = menuObj.transform.Find("GripAccent");
            GripAccent.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            GripAccent.GetComponent<Renderer>().material.color = accentColor;

            // Extra sakura coloring
            Transform Pole1 = menuObj.transform.Find("Pole1");
            Pole1.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            Pole1.GetComponent<Renderer>().material.color = mainColor;

            Transform Pole2 = menuObj.transform.Find("Pole2");
            Pole2.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            Pole2.GetComponent<Renderer>().material.color = mainColor;

            Transform PipeTop1 = menuObj.transform.Find("PipeTop1");
            PipeTop1.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            PipeTop1.GetComponent<Renderer>().material.color = borderColor;

            Transform PipeTop2 = menuObj.transform.Find("PipeTop2");
            PipeTop2.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            PipeTop2.GetComponent<Renderer>().material.color = borderColor;

            Transform UnderBar = menuObj.transform.Find("UnderBar");
            UnderBar.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            UnderBar.GetComponent<Renderer>().material.color = borderColor;

            Transform BarConnector = menuObj.transform.Find("BarConnector");
            BarConnector.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            BarConnector.GetComponent<Renderer>().material.color = mainColor;

            Transform TopUnderBar = menuObj.transform.Find("BarConnector");
            BarConnector.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            BarConnector.GetComponent<Renderer>().material.color = mainColor;

            Transform TopBarUnder = menuObj.transform.Find("TopBarUnder");
            TopBarUnder.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            TopBarUnder.GetComponent<Renderer>().material.color = mainColor;

            Transform TopBar = menuObj.transform.Find("TopBar");
            TopBar.GetComponent<Renderer>().material.shader = Shader.Find("GorillaTag/UberShader");
            TopBar.GetComponent<Renderer>().material.color = accentColor;
        }

        void CreateButton(float zOffset, string btnName)
        {
            GameObject btn = Instantiate(btnPrefab);
            GameObject btnOutline = Instantiate(btnPrefab);

            //GameObject btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            btn.transform.localScale = Vector3.one * 0.78f; //new Vector3(0.015f, 0.265f, 0.04f)
            btn.transform.localRotation = Quaternion.identity;
            btn.layer = 18;

            btnOutline.transform.localScale = Vector3.one * 0.79f; //new Vector3(0.015f, 0.265f, 0.04f)
            btnOutline.transform.localRotation = Quaternion.identity;

            var follow = btn.AddComponent<FollowMenu>();
            follow.target = GTPlayer.Instance.LeftHand.controllerTransform;
            follow.position = new Vector3(0.026f, 0f, zOffset) + menuHandOffset + menuGripPosition;
            follow.rotation = Quaternion.identity * Quaternion.Euler(270f, 0f, 0f);

            var followOutline = btnOutline.AddComponent<FollowMenu>();
            followOutline.target = GTPlayer.Instance.LeftHand.controllerTransform;
            followOutline.position = new Vector3(0.025f, 0f, zOffset) + menuHandOffset + menuGripPosition;
            followOutline.rotation = Quaternion.identity * Quaternion.Euler(270f, 0f, 0f);

            var trigger = btn.AddComponent<ButtonTrigger>();
            trigger.btnIdentifier = btnName;
            trigger.pressButtonSoundIndex = 28;

            var collider = btn.AddComponent<BoxCollider>();
            collider.isTrigger = true;

            var renderer = btn.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                renderer.material.shader = Shader.Find("GorillaTag/UberShader");
                renderer.material.color = buttonColor;
            }

            var rendererOutline = btnOutline.GetComponentInChildren<Renderer>();
            if (rendererOutline != null)
            {
                rendererOutline.material.shader = Shader.Find("GorillaTag/UberShader");
                rendererOutline.material.color = buttonOutlineColor;
            }

            GameObject textObj = new GameObject("ButtonLabel");
            textObj.transform.SetParent(btn.transform);
            textObj.transform.localPosition = new Vector3(0.023f, 0f, 0f); //new Vector3(0.58f, 0f, 0f);
            textObj.transform.localRotation = Quaternion.Euler(0f, -90f, 180f);

            var text = textObj.AddComponent<TextMeshPro>();
            text.text = btnName.ToUpper();
            text.fontSize = 20;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.font = VRRig.LocalRig.playerText1.font;
            text.enableAutoSizing = false;
            //text.rectTransform.sizeDelta = new Vector2(100f, 40f);
            text.transform.localScale = new Vector3(0.02f, 0.018f, 2f);

            btnObjs.Add(btn);
        }

        void DestroyButtons()
        {
            foreach (GameObject btnObj in btnObjs)
            {
                Destroy(btnObj);
            }
        }

        void LoadBundles()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream("ThatUtilsPad.Assets.tup-prefab"))
            {
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                menuBundle = AssetBundle.LoadFromMemory(buffer);
            }

            using (Stream stream = assembly.GetManifestResourceStream("ThatUtilsPad.Assets.tupsakura-prefab"))
            {
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                sakuraBundle = AssetBundle.LoadFromMemory(buffer);
            }

            using (Stream stream = assembly.GetManifestResourceStream("ThatUtilsPad.Assets.tupbutton-prefab"))
            {
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                buttonBundle = AssetBundle.LoadFromMemory(buffer);
            }

            foreach (string name in buttonBundle.GetAllAssetNames())
            {
                Debug.Log("BUNDLE ASSET: " + name);
            }
        }

    }

    public class ButtonTrigger : GorillaPressableButton
    {
        public string btnIdentifier;
        public override void ButtonActivationWithHand(bool isLeftHand)
        {
            base.ButtonActivationWithHand(isLeftHand);

            if (!isLeftHand)
            {
                if (Mods.Actions.TryGetValue(btnIdentifier, out var action))
                {
                    action.Invoke();
                }
                else
                {
                    Debug.LogWarning($"[TUP: WARNING] No mod found for button: {btnIdentifier}");
                }
            }
        }
    }

    public class FollowMenu : MonoBehaviour
    {
        public Transform target;
        public Vector3 position;
        public Quaternion rotation;

        void LateUpdate()
        {
            if (target == null) return;

            transform.position = target.TransformPoint(position);
            transform.rotation = target.rotation * rotation;
        }
    }
}
