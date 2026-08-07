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
    }
}
