using CoolerMenu.Common.Utils;
using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace CoolerMenu.Common.Config;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CA2211 // Non-constant fields should not be visible.

public class CoolerMenuConfig : ModConfig
{
        // 'ConfigManager.Add' Automatically sets public fields named 'Instance' to the ModConfig's type.
    public static CoolerMenuConfig Instance;

    public override ConfigScope Mode =>
        ConfigScope.ClientSide;

    #region Appearance

    [Header("Appearance")]

    [DefaultValue(HorizontalPosition.Left)]
    public HorizontalPosition ButtonPosition;

    [DefaultValue(HorizontalPosition.Left)]
    public HorizontalPosition LogoPosition;

    [DefaultValue(true)]
    public bool EnableHoverBackgrounds;

    #endregion

    #region Icons

    [Header("Icons")]

    [DefaultValue(true)]
    public bool EnableButtonIcons;

    [DefaultValue(true)]
    public bool EnableSplashMessages;

    #endregion

    #region Animations

    [Header("Animations")]

    [DefaultValue(true)]
    public bool EnableFloatingAnimation;

    #endregion

    #region CoreMenuConfig

    [Header("CoreMenuConfig")]

    [DefaultValue(true)]
    public bool IsMenuActive;

    #endregion
}
