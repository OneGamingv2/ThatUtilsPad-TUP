using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using GorillaLocomotion;
using GorillaNetworking;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using PlayFab;
using ThatUtilsPad.MenuComponents;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad;


public partial class Main
{
    private bool ConsumeMenuOpenBindPress(bool pressedThisFrame)
    {
        if (!pressedThisFrame)
            return false;

        if (!requireDoubleClickOpen)
            return true;

        if (Time.time - lastMenuOpenBindPressTime <= MenuOpenDoubleClickWindow)
        {
            lastMenuOpenBindPressTime = -10f;
            return true;
        }

        lastMenuOpenBindPressTime = Time.time;
        return false;
    }

    public void SetMenuOpenOptions(bool doubleClickRequired, string bindCode)
    {
        requireDoubleClickOpen = doubleClickRequired;
        menuOpenBindCode = string.IsNullOrWhiteSpace(bindCode) ? DefaultMenuOpenBindCode : bindCode;
        previousControllerState = IsMenuOpenBindPressed();
        lastMenuOpenBindPressTime = -10f;
    }

    private bool IsMenuOpenBindPressed() => IsMenuOpenBindPressed(menuOpenBindCode);

    public static bool IsMenuOpenBindPressed(string bindCode)
    {
        if (string.IsNullOrWhiteSpace(bindCode))
            bindCode = DefaultMenuOpenBindCode;

        if (bindCode.StartsWith("InputSystem:", StringComparison.OrdinalIgnoreCase))
        {
            string path = bindCode.Substring("InputSystem:".Length);
            return IsInputSystemButtonPressed(path);
        }

        ControllerInputPoller poller = ControllerInputPoller.instance;
        if (poller == null)
            return false;

        return bindCode switch
        {
            "Left:JoystickButton" => IsControllerInputSystemButtonPressed(true, "primary2daxisclick", "thumbstickclicked", "joystickclick", "stickclick"),
            "Left:SecondaryButton" => poller.leftControllerSecondaryButton,
            "Left:PrimaryButton" => poller.leftControllerPrimaryButton,
            "Left:TriggerButton" => poller.leftControllerTriggerButton || poller.leftControllerIndexFloat > 0.75f,
            "Left:GripButton" => poller.leftControllerGripFloat > 0.75f,
            "Right:JoystickButton" => IsControllerInputSystemButtonPressed(false, "primary2daxisclick", "thumbstickclicked", "joystickclick", "stickclick"),
            "Right:SecondaryButton" => poller.rightControllerSecondaryButton,
            "Right:PrimaryButton" => poller.rightControllerPrimaryButton,
            "Right:TriggerButton" => poller.rightControllerTriggerButton || poller.rightControllerIndexFloat > 0.75f,
            "Right:GripButton" => poller.rightControllerGripFloat > 0.75f,
            _ => false
        };
    }

    public static bool TryGetPressedMenuOpenBind(HashSet<string> ignoredCodes, out string bindCode, out string displayName)
    {
        foreach (string bindMaybe in BuiltInMenuOpenBindCodes)
        {
            if (ignoredCodes != null && ignoredCodes.Contains(bindMaybe))
                continue;

            if (!IsMenuOpenBindPressed(bindMaybe))
                continue;

            bindCode = bindMaybe;
            displayName = GetMenuOpenBindDisplayName(bindMaybe);
            return true;
        }

        foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
        {
            if (device == null || device is Keyboard || device is Mouse)
                continue;

            foreach (UnityEngine.InputSystem.InputControl control in device.allControls)
            {
                if (control is not UnityEngine.InputSystem.Controls.ButtonControl button)
                    continue;

                if (IsIgnoredInputButton(button))
                    continue;

                string code = "InputSystem:" + button.path;
                if (ignoredCodes != null && ignoredCodes.Contains(code))
                    continue;

                if (!button.isPressed)
                    continue;

                bindCode = code;
                displayName = FormatInputSystemButtonName(button);
                return true;
            }
        }

        bindCode = "";
        displayName = "";
        return false;
    }

    public static HashSet<string> GetPressedMenuOpenBinds()
    {
        HashSet<string> pressed = new HashSet<string>();

        foreach (string bindMaybe in BuiltInMenuOpenBindCodes)
        {
            if (IsMenuOpenBindPressed(bindMaybe))
                pressed.Add(bindMaybe);
        }

        foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
        {
            if (device == null || device is Keyboard || device is Mouse)
                continue;

            foreach (UnityEngine.InputSystem.InputControl control in device.allControls)
            {
                if (control is not UnityEngine.InputSystem.Controls.ButtonControl button)
                    continue;

                if (IsIgnoredInputButton(button) || !button.isPressed)
                    continue;

                pressed.Add("InputSystem:" + button.path);
            }
        }

        return pressed;
    }

    public static string GetMenuOpenBindDisplayName(string bindCode)
    {
        if (string.IsNullOrWhiteSpace(bindCode))
            bindCode = DefaultMenuOpenBindCode;

        if (bindCode.StartsWith("InputSystem:", StringComparison.OrdinalIgnoreCase))
        {
            string path = bindCode.Substring("InputSystem:".Length);
            UnityEngine.InputSystem.InputControl control = InputSystem.FindControl(path);
            if (control is UnityEngine.InputSystem.Controls.ButtonControl button)
                return FormatInputSystemButtonName(button);

            return path;
        }

        return bindCode switch
        {
            "Left:JoystickButton" => "Left Joystick",
            "Left:SecondaryButton" => "Left Secondary",
            "Left:PrimaryButton" => "Left Primary",
            "Left:TriggerButton" => "Left Trigger",
            "Left:GripButton" => "Left Grip",
            "Right:JoystickButton" => "Right Joystick",
            "Right:SecondaryButton" => "Right Secondary",
            "Right:PrimaryButton" => "Right Primary",
            "Right:TriggerButton" => "Right Trigger",
            "Right:GripButton" => "Right Grip",
            _ => "Left Secondary"
        };
    }

    private static bool IsInputSystemButtonPressed(string path)
    {
        UnityEngine.InputSystem.InputControl control = InputSystem.FindControl(path);
        return control is UnityEngine.InputSystem.Controls.ButtonControl button && button.isPressed;
    }
    private static bool IsControllerInputSystemButtonPressed(bool leftHand, params string[] controlNames)
    {
        foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
        {
            if (device == null || device is Keyboard || device is Mouse)
                continue;

            bool matchesHand = device.usages.Any(usage => usage.ToString().IndexOf(leftHand ? "LeftHand" : "RightHand", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!matchesHand)
                continue;

            foreach (UnityEngine.InputSystem.InputControl control in device.allControls)
            {
                if (control is not UnityEngine.InputSystem.Controls.ButtonControl button || !button.isPressed)
                    continue;

                string name = (button.name ?? "").Replace(" ", "").ToLowerInvariant();
                string path = (button.path ?? "").Replace(" ", "").ToLowerInvariant();
                foreach (string controlName in controlNames)
                {
                    string squished = (controlName ?? "").Replace(" ", "").ToLowerInvariant();
                    if (name.Contains(squished) || path.Contains(squished))
                        return true;
                }
            }
        }

        string hand = leftHand ? "LeftHand" : "RightHand";
        return IsInputSystemButtonPressed("<XRController>{" + hand + "}/primary2DAxisClick") ||
               IsInputSystemButtonPressed("<XRController>{" + hand + "}/thumbstickClicked");
    }

    private static bool IsIgnoredInputButton(UnityEngine.InputSystem.Controls.ButtonControl button)
    {
        string name = (button.name ?? "").ToLowerInvariant();
        string path = (button.path ?? "").ToLowerInvariant();
        return name.Contains("touch") || path.Contains("touch") || name.Contains("position") || path.Contains("position");
    }

    private static string FormatInputSystemButtonName(UnityEngine.InputSystem.Controls.ButtonControl button)
    {
        string deviceName = button.device?.displayName;
        if (string.IsNullOrWhiteSpace(deviceName))
            deviceName = button.device?.name ?? "Controller";

        string controlName = !string.IsNullOrWhiteSpace(button.displayName) ? button.displayName : button.name;
        if (string.IsNullOrWhiteSpace(controlName))
            controlName = button.path;

        return deviceName + " " + controlName;
    }

    private static readonly string[] BuiltInMenuOpenBindCodes =
    {
        "Left:JoystickButton",
        "Left:SecondaryButton",
        "Left:PrimaryButton",
        "Left:TriggerButton",
        "Left:GripButton",
        "Right:JoystickButton",
        "Right:SecondaryButton",
        "Right:PrimaryButton",
        "Right:TriggerButton",
        "Right:GripButton",
    };
}
