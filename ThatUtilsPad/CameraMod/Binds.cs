using System.Collections.Generic;
using CameraMod.Camera.Comps;

namespace CameraMod
{
    public class Binds
    {
        private static readonly HashSet<string> bindAliases = new HashSet<string>();

        public static void Init()
        {
            bindAliases.Clear();
            string activate = "RP";
            try
            {
                if (Configs.controls?.CurrentSettings != null &&
                    !string.IsNullOrWhiteSpace(Configs.controls.CurrentSettings.activateBind))
                    activate = Configs.controls.CurrentSettings.activateBind;
            }
            catch
            {
            }

            foreach (string bindAlias in activate.Split(' '))
            {
                if (!string.IsNullOrWhiteSpace(bindAlias))
                    bindAliases.Add(bindAlias.Trim());
            }

            if (Configs.controls != null)
            {
                Configs.controls.Changed += cfg =>
                {
                    bindAliases.Clear();
                    if (cfg == null || string.IsNullOrWhiteSpace(cfg.activateBind))
                        return;
                    foreach (string bindAlias in cfg.activateBind.Split(' '))
                    {
                        if (!string.IsNullOrWhiteSpace(bindAlias))
                            bindAliases.Add(bindAlias.Trim());
                    }
                };
            }
        }

        public static bool Tablet()
        {
            InputManager inputManager = InputManager.instance;
            if (inputManager == null)
                return false;

            foreach (string bindAlias in bindAliases)
            {
                if (bindAlias == "RP" && inputManager.RightPrimaryButton) return true;
                if (bindAlias == "LP" && inputManager.LeftPrimaryButton) return true;
                if (bindAlias == "RS" && inputManager.RightSecondaryButton) return true;
                if (bindAlias == "LS" && inputManager.LeftSecondaryButton) return true;
            }

            return false;
        }

        private static readonly string[] ActivateCycle = { "RP", "LP", "RS", "LS" };

        public static string GetActivateBind()
        {
            try
            {
                string bind = Configs.controls?.CurrentSettings?.activateBind;
                if (!string.IsNullOrWhiteSpace(bind))
                    return bind.Trim().Split(' ')[0];
            }
            catch
            {
            }

            return "RP";
        }

        public static string GetActivateBindLabel()
        {
            switch (GetActivateBind())
            {
                case "LP": return "LP  Left A/X";
                case "RS": return "RS  Right B/Y";
                case "LS": return "LS  Left B/Y";
                default: return "RP  Right A/X";
            }
        }

        public static string CycleActivateBind()
        {
            string current = GetActivateBind();
            int idx = 0;
            for (int i = 0; i < ActivateCycle.Length; i++)
            {
                if (ActivateCycle[i] == current)
                {
                    idx = i;
                    break;
                }
            }

            string next = ActivateCycle[(idx + 1) % ActivateCycle.Length];
            try
            {
                ConfigType cfg = Configs.controls?.CurrentSettings ?? new ConfigType();
                cfg.activateBind = next;
                Configs.controls?.Save(cfg);
            }
            catch
            {
            }

            Init();
            return next;
        }
    }
}
