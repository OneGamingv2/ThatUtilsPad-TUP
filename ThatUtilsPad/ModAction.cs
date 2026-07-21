using System;
using System.Collections.Generic;

namespace ThatUtilsPad;

public class ModAction
{
    public Action Action;
    public bool IsToggle;
    public float Cooldown;

    public ModAction(Action action, bool isToggle = false, bool hasCooldown = false, float cooldown = 0f)
    {
        Action = action;
        IsToggle = isToggle;
        Cooldown = hasCooldown ? cooldown : 0f;
    }

    public static implicit operator ModAction(Action action) => new ModAction(action);
}

public class ModCategory
{
    public string ImageName;
    public Dictionary<string, ModAction> Actions;
    public bool ShowInMenu;

    public ModCategory(string imageName, bool showInMenu = true)
    {
        ImageName = imageName;
        ShowInMenu = showInMenu;
        Actions = new Dictionary<string, ModAction>();
    }
}
