using CoolerMenu.Common.Config;
using CoolerMenu.Common.Utils;
using CoolerMenu.GeneratedAssets.DataStructures;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.Cil;
using ReLogic.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Chat;
using tModPorter;

namespace CoolerMenu.Common.Menu;

public sealed class MenuSystem : ModSystem
{
    #region Private Fields

    private const int CoolerMenuID = 1007;

    private static RenderTarget2D TransitionTarget;

    private const float ButtonX = 70f;

        // TODO: Generalize the below into a ButtonData struct of sorts.
    private static readonly Vector2[] ButtonPositions =
    [
        new(ButtonX, 300f),
        new(ButtonX, 360f),
        new(ButtonX, 420f),
        new(ButtonX, 480f),
        new(ButtonX, 540f),
        new(ButtonX, 600f)
    ];

    private static readonly LocalizedText[] ButtonLabels =
    [
        Language.GetText("Mods.CoolerMenu.SinglePlayer"),
        Language.GetText("Mods.CoolerMenu.Multiplayer"),
        Language.GetText("Mods.CoolerMenu.Achievements"),
        Language.GetText("Mods.CoolerMenu.Settings"),
        Language.GetText("Mods.CoolerMenu.Workshop"),
        Language.GetText("Mods.CoolerMenu.Exit")
    ];

    private static readonly LazyAsset<Texture2D>[] IconAssets =
    [
        IconTextures.Singleplayer,
        IconTextures.Multiplayer,
        IconTextures.Achievements,
        IconTextures.Settings,
        IconTextures.Workshop,
    ];

    private static readonly LazyAsset<Texture2D>[] BackgroundAssets =
    [
        BackgroundTextures.Singleplayer,
        BackgroundTextures.Multiplayer,
        BackgroundTextures.Achievements,
        BackgroundTextures.Settings,
        BackgroundTextures.Workshop,
    ];

    private static readonly Action[] ButtonActions =
    [
        () => StartTransition(() => Main.menuMode = 1),
        () => StartTransition(() => Main.menuMode = 12),
        () => StartTransition(OpenAchievements),
        () => StartTransition(() => Main.menuMode = 11),
        () => StartTransition(OpenWorkshop),
        Main.instance.Exit,
    ];

    private const float IconOffset = 30f;

    private static int HoveredButton = -1;
    private static readonly bool[] HoveredButtons = new bool[ButtonLabels.Length];

    private const float BackgroundFadeSpeed = .05f;

    private static readonly float[] BackgroundOpacities = new float[5];

    private const string SplashKey = "Mods.CoolerMenu.SplashMessages.Splash";

    private static readonly LocalizedText[] SplashTexts =
        Language.FindAll(Lang.CreateDialogFilter(SplashKey, null));

    private static LocalizedText CurrentSplash = null;

    private const int SplashChangeTime = 600;
    private static int SplashChangeTimer = 0;

    #region Animations

        // Smoother slide animation
    private const float SlideDuration = 80f;
    private static float SlideOffset = -300f;
    private static float SlideProgress = 0f;

    private static bool AnimateIn = false;

    private static bool IsTransitioning = false;

    private const int TransitionDelay = 30;
    private static int TransitionTimer = 0;

        // Add these new fields for better menu handling
    private const int FramesBeforeSwitch = 2;
    private static int TimeInNormalMenu = 0;

    private const float BobIncrement = .05f;

    private static float BobTimer = 0f;

    private static readonly float[] BobOffsets = new float[ButtonLabels.Length];

    #endregion

    private static bool SetCoolerMenu = false;

    private static string TempMessage = "";
    private static float TempMessageTimer = 0f;

    #endregion

    #region Loading

    public override void Load()
    {
        Main.QueueMainThreadAction(() =>
        {
            IL_MenuLoader.UpdateAndDrawModMenuInner += ModifyLogoPosition;

            IL_Main.DrawMenu += ModifyVanillaLogo;

            On_UserInterface.Draw += CaptureInterface;

            Main.graphics.GraphicsDevice.PresentationParameters.RenderTargetUsage = RenderTargetUsage.PreserveContents;
            Main.graphics.ApplyChanges();
        });
    }

    public override void Unload()
    {
        Main.QueueMainThreadAction(() =>
        {
            IL_MenuLoader.UpdateAndDrawModMenuInner -= ModifyLogoPosition;

            IL_Main.DrawMenu -= ModifyVanillaLogo;

            On_UserInterface.Draw -= CaptureInterface;

            TransitionTarget?.Dispose();
        });
    }

    #endregion

    #region Logo Position

    private void ModifyLogoPosition(ILContext il)
    {
        ILCursor c = new(il);

        int logoPositionIndex = -1;

            // Match to just before the calling of PreDrawLogo.
        c.GotoNext(MoveType.Before,
            i => i.MatchLdsfld(typeof(MenuLoader).FullName, nameof(MenuLoader.currentMenu)),
            i => i.MatchLdarg(out _),
            i => i.MatchLdloca(out logoPositionIndex),
            i => i.MatchLdarga(out _),
            i => i.MatchLdloca(out _),
            i => i.MatchLdarga(out _),
            i => i.MatchCallvirt<ModMenu>(nameof(ModMenu.PreDrawLogo)));

            // Load a ref to the local.
        c.EmitLdloca(logoPositionIndex);

            // Emit a delegate, editing the logo position through modifying the ref.
        c.EmitDelegate((ref Vector2 logoDrawPos) =>
        {
            if (ModLoader.isLoading)
                return;

            CoolerMenuConfig config = CoolerMenuConfig.Instance;

            if (!config.IsMenuActive)
                return;

            float xPos = config.LogoPosition == HorizontalPosition.Left ? 260f : Main.screenWidth - 260f;

            logoDrawPos = new(xPos, Main.screenHeight * 0.18f);

            Main.instance.logoScale = 1.2f;

            UpdateMenu();
        });

            // Goto past the post draw
        c.GotoNext(MoveType.After,
            i => i.MatchCallvirt<ModMenu>(nameof(ModMenu.PostDrawLogo)));

            // Render extra bits such as the toggles and the buttons
        c.EmitDelegate(() =>
        {
            if (ModLoader.isLoading)
                return;

            RenderVanillaMenuToggle();
            RenderCoreMenuToggle();

            PostDrawMenu(Main.spriteBatch);
        });
    }

    #endregion

    #region Vanilla Logo

    private void ModifyVanillaLogo(ILContext il)
    {
        ILCursor c = new(il);

            // Vanilla draws the logo 4 times.
        for (int j = 0; j < 4; j++)
        {
                //Move to after the positioning of the draw
            c.GotoNext(MoveType.After,
                i => i.MatchLdsfld<Main>(nameof(Main.screenWidth)),
                i => i.MatchLdcI4(2),
                i => i.MatchDiv(),
                i => i.MatchConvR4(),
                i => i.MatchLdcR4(100),
                i => i.MatchNewobj<Vector2>(".ctor"));

                // Modify the position (in old pos, out new pos.)
            c.EmitDelegate((Vector2 currentLogoPos) =>
            {
                if (ModLoader.isLoading)
                    return currentLogoPos;

                CoolerMenuConfig config = CoolerMenuConfig.Instance;

                if (!config.IsMenuActive)
                    return currentLogoPos;

                float xPos = config.LogoPosition == HorizontalPosition.Left ? 260f : Main.screenWidth - 260f;

                return new(xPos, Main.screenHeight * 0.18f);

            });
        }
    }

    #endregion

    #region Menu Toggles

    private static void RenderVanillaMenuToggle()
    {
        if (Main.menuMode != 0 &&
            Main.menuMode != CoolerMenuID)
            return;

        CoolerMenuConfig config = CoolerMenuConfig.Instance;

        if (!config.IsMenuActive)
            return;

            // Re-render and re-implement the menu switcher
        ModMenu currentMenu = MenuLoader.CurrentMenu;
        List<ModMenu> menus = MenuLoader.menus;

        bool notifyNewMainMenuThemes = ModLoader.notifyNewMainMenuThemes;
        int newMenus;

        if (menus is null ||
            menus.Count <= 0)
            return;

        lock (menus)
            newMenus = menus.Count((m) => m.IsNew);

        byte b = (byte)((255 + Main.tileColor.R * 2) / 3);

        Color color = new(b, b, b, 255);

        string text =
            Language.GetTextValue("tModLoader.ModMenuSwap") +
            ": " + currentMenu.DisplayName +
            (newMenus == 0 ? "" :
            notifyNewMainMenuThemes ?
            $" ({newMenus} New)" : "");

        Vector2 menuTextSize =
            ChatManager.GetStringSize(FontAssets.MouseText.Value, [.. ChatManager.ParseMessage(text, color)], Vector2.One);

        Rectangle switchTextRect =
            new((int)(Main.screenWidth * .5f - menuTextSize.X * .5f), (int)(Main.screenHeight - 2 - menuTextSize.Y),
            (int)menuTextSize.X, (int)menuTextSize.Y);

        if (switchTextRect.Contains(Main.mouseX, Main.mouseY) &&
            !Main.alreadyGrabbingSunOrMoon)
        {
            if (Main.mouseLeftRelease && Main.mouseLeft)
            {
                SoundEngine.PlaySound(in SoundID.MenuTick);
                MenuLoader.OffsetModMenu(1);
            }
            else if (Main.mouseRightRelease && Main.mouseRight)
            {
                SoundEngine.PlaySound(in SoundID.MenuTick);
                MenuLoader.OffsetModMenu(-1);
            }
        }

        ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value, text, new Vector2(switchTextRect.X, switchTextRect.Y), switchTextRect.Contains(Main.mouseX, Main.mouseY) ? Main.OurFavoriteColor : new Color(120, 120, 120, 76), 0f, Vector2.Zero, Vector2.One);
    }

    private static void RenderCoreMenuToggle()
    {
        if (Main.menuMode != 0 && Main.menuMode != 1007)
            return;

        CoolerMenuConfig config = CoolerMenuConfig.Instance;

        byte b = (byte)((255 + Main.tileColor.R * 2) / 3);

        Color color = new(b, b, b, 255);

        string text =
            Language.GetTextValue("Mods.CoolerMenu.CoreMenu.CoreMenuSwap") +
            (config.IsMenuActive ?
            Language.GetTextValue("Mods.CoolerMenu.CoreMenu.CoolerMenu") :
            Language.GetTextValue("Mods.CoolerMenu.CoreMenu.Vanilla"));

        Vector2 menuTextSize =
            ChatManager.GetStringSize(FontAssets.MouseText.Value, [.. ChatManager.ParseMessage(text, color)], Vector2.One);

        Rectangle switchTextRect =
            new((int)(Main.screenWidth * .5f - menuTextSize.X * .5f), (int)(Main.screenHeight - 2 - menuTextSize.Y * 2f),
            (int)menuTextSize.X, (int)menuTextSize.Y);

        ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value, text, new Vector2(switchTextRect.X, switchTextRect.Y), switchTextRect.Contains(Main.mouseX, Main.mouseY) ? Main.OurFavoriteColor : new Color(120, 120, 120, 76), 0f, Vector2.Zero, Vector2.One);

        if (!switchTextRect.Contains(Main.mouseX, Main.mouseY) ||
            Main.alreadyGrabbingSunOrMoon ||
            !(Main.mouseLeftRelease && Main.mouseLeft ||
            Main.mouseRightRelease && Main.mouseRight))
            return;

        SoundEngine.PlaySound(in SoundID.MenuTick);

        config.IsMenuActive = !config.IsMenuActive;

        config.SaveChanges();

        SetCoolerMenu = false;

        if (!config.IsMenuActive &&
            Main.menuMode == CoolerMenuID)
            Main.menuMode = 0;
    }

    #endregion

    #region Interface Transitioning

    private static void CaptureInterface(On_UserInterface.orig_Draw orig, UserInterface self, SpriteBatch spriteBatch, GameTime time)
    {
        CoolerMenuConfig config = CoolerMenuConfig.Instance;

        if (self != Main.MenuUI ||
            !config.IsMenuActive ||
            Main.menuMode == 0 ||
            Main.menuMode == CoolerMenuID)
        {
            orig(self, spriteBatch, time);
            return;
        }

            // Update our menu.
        if (IsTransitioning)
            UpdateMenu();

        float eased = Easings.InOutCubic(SlideProgress);

            // PlayerInput.SetZoom_UI(); - ??
            // ApplyScaleMatrix(); -  Whoever wrote this initially did NOT know what they were doing. :sob:

            // Draw our menu.
        int lastMenuMode = Main.menuMode;

        Main.menuMode = 0;

        PostDrawMenu(spriteBatch);

        Main.menuMode = lastMenuMode;

        GraphicsDevice device = Main.instance.GraphicsDevice;

        Viewport viewport = device.Viewport;

        spriteBatch.End();

        using (new RenderTargetSwap(ref TransitionTarget, viewport.Width, viewport.Height))
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);

            orig(self, spriteBatch, time);

            spriteBatch.End();
        }

            // TODO: SpriteBatchSnapshot impl and or DAYBREAK dependancy.
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Matrix.Identity);

        Vector2 position = Vector2.UnitX * eased * (config.ButtonPosition == HorizontalPosition.Left ? 200f : -200f);

        Color color = Color.White * MathF.Sqrt(1f - eased);

            // Why was this in a try-catch block??
        spriteBatch.Draw(TransitionTarget, position, color);

        spriteBatch.End();
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);

            // PlayerInput.SetZoom_UI();
            // ApplyScaleMatrix(); - Stop.
    }

    #endregion

    #region Splash Text

    private static LocalizedText GetRandomSplashMessage()
    {
        if (SplashTexts.Length > 0)
            return SplashTexts[Main.rand.Next(SplashTexts.Length)];

        return null;
    }

    #endregion

    #region Buttons

    private static int GetHoveredButton() =>
        HoveredButtons.IndexOf(true);

    private static void OpenWorkshop()
    {
        try
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            Main.menuMode = 888;

            UIWorkshopHub workshopHub = new(null);
            workshopHub.EnterHub();
            Main.MenuUI.SetState(workshopHub);
        }
        catch
        {
            ShowTemporaryMessage("Failed to open Workshop");
        }
    }

    private static void OpenAchievements()
    {
        try
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            Main.menuMode = 888;
            Main.MenuUI.SetState(Main.AchievementsMenu);
        }
        catch
        {
            ShowTemporaryMessage("Failed to open Achievements");
        }
    }

    #endregion

    #region Updating

    public static void UpdateMenu()
    {
        CoolerMenuConfig config = CoolerMenuConfig.Instance;

        if (!config.IsMenuActive)
            return;

        if (!SetCoolerMenu)
            ResetCoolerMenu();

            // Auto-switch to custom menu mode.
        if (Main.menuMode == 0)
        {
            if (++TimeInNormalMenu >= FramesBeforeSwitch &&
                !IsTransitioning)
            {
                Main.menuMode = 1007;
                TimeInNormalMenu = 0;
            }
        }
        else
            TimeInNormalMenu = 0;

            // Handle transitions.
        UpdateTransitions();

            // Update temporary message.
        if (TempMessageTimer > 0)
            TempMessageTimer--;

            // Bob animation.
        BobTimer += BobIncrement;

            // Update splash message.
        if (config.EnableSplashMessages)
        {
            SplashChangeTimer++;

            if (SplashChangeTimer >= SplashChangeTime)
            {
                CurrentSplash = GetRandomSplashMessage();
                SplashChangeTimer = 0;
            }

                // Initialize first splash message.
            CurrentSplash ??= GetRandomSplashMessage();
        }
        else
            CurrentSplash = null;

        if (config.EnableHoverBackgrounds)
            UpdateBackgrounds();

            // Slide in when in custom menu mode.
        bool shouldShowButtons =
            Main.menuMode == CoolerMenuID &&
            !AnimateIn &&
            !IsTransitioning;
        if (shouldShowButtons)
        {
            AnimateIn = true;
            SlideProgress = 0f;
        }
    }

    #region Transitions

        // TODO: Rewrite this a bit cleaner.
    private static void UpdateTransitions()
    {
        if (IsTransitioning)
        {
            TransitionTimer++;

            if (TransitionTimer < TransitionDelay)
            {
                    // Slide out animation
                SlideProgress = Math.Max(SlideProgress - 1f / (SlideDuration * .5f), 0f);

                float eased = Easings.InOutCubic(SlideProgress);

                SlideOffset = -300f + eased * 300f;
            }
            else if (TransitionTimer >= TransitionDelay)
            {
                    // Complete transition - execute the delayed action
                IsTransitioning = false;
                TransitionTimer = 0;

                return;
            }
        }
        else
        {
            if (AnimateIn &&
                SlideProgress < 1f)
                SlideProgress += 1f / (SlideDuration * 1.5f);

            if (!AnimateIn &&
                SlideProgress > 0f)
                SlideProgress -= 1f / SlideDuration;

            SlideProgress = Utilities.Saturate(SlideProgress);

            float eased = Easings.InOutCubic(SlideProgress);
            SlideOffset = -300f + eased * 300f;
        }
    }

    private static void StartTransition(Action action)
    {
        IsTransitioning = true;
        TransitionTimer = 0;
        AnimateIn = false;

            // Unsure.
        action();
            // DelayedAction = null;
    }

    #endregion

    #region Hover Backgrounds

    private static void UpdateBackgrounds()
    {
        for (int i = 0; i < BackgroundOpacities.Length; i++)
            BackgroundOpacities[i] = Math.Max(BackgroundOpacities[i] - BackgroundFadeSpeed, 0f);

        int newlyHovered = GetHoveredButton();

        if (newlyHovered != -1)
            HoveredButton = newlyHovered;

            // If not hovering any button, keep the last background visible but fading.
        if (newlyHovered == -1 && HoveredButton != -1)
            BackgroundOpacities[HoveredButton] =
                Math.Max(BackgroundOpacities[newlyHovered] - BackgroundFadeSpeed * 0.3f, .3f);
        else if (newlyHovered != -1)
            BackgroundOpacities[newlyHovered] =
                Math.Min(BackgroundOpacities[newlyHovered] + BackgroundFadeSpeed * 2f, .9f);
        else if (HoveredButton == -1)
            for (int i = 0; i < BackgroundOpacities.Length; i++)
                BackgroundOpacities[i] = 0;
    }

    #endregion

    private static void ResetCoolerMenu()
    {
        Main.menuMode = 0;

        HoveredButton = -1; // Reset hover state

        for (int i = 0; i < BobOffsets.Length; i++)
            BobOffsets[i] = (float)(Main.rand.NextDouble() * Math.PI * 2);

        SlideOffset = -300f;
        SlideProgress = 0f;
        AnimateIn = true;
        IsTransitioning = false;
        TransitionTimer = 0;
        TimeInNormalMenu = 0;
            // DelayedAction = null;

        SetCoolerMenu = true;
    }

    #endregion

    #region Drawing

    public static void PostDrawMenu(SpriteBatch spriteBatch)
    {
        if (Main.menuMode != 0 &&
            Main.menuMode != CoolerMenuID)
            return;

        CoolerMenuConfig config = CoolerMenuConfig.Instance;

        if (!config.IsMenuActive)
            return;

            // Draw splash text messages.
        if (config.EnableSplashMessages && CurrentSplash != null)
            DrawSplash(spriteBatch);

            // Draw hover backgrounds.
        if (config.EnableHoverBackgrounds)
            DrawBackgrounds(spriteBatch);

    // TODO: Make everything play nicely in a nullable context.
#nullable enable

            // Draw buttons if on-screen.
        if (SlideOffset > -250f)
        {
            for (int i = 0; i < ButtonLabels.Length; i++)
            {
                Texture2D? icon = null;

                if (config.EnableButtonIcons && i < IconAssets.Length)
                    icon = IconAssets[i];

                DrawButton(spriteBatch, icon, ButtonLabels[i].Value, ButtonPositions[i], BobOffsets[i], config.ButtonPosition, ref HoveredButtons[i], ButtonActions[i]);
            }
        }

            // Draw temporary message.
        if (TempMessageTimer > 0)
        {
            DynamicSpriteFont font = FontAssets.MouseText.Value;
            Vector2 size = font.MeasureString(TempMessage) * 1.5f;

            Vector2 pos = new(Main.screenWidth * .5f - size.X * .5f, Main.screenHeight - 100);

            Color color = Main.OurFavoriteColor;

            ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, TempMessage, pos, color, 0f, Vector2.Zero, new Vector2(1.5f));
        }

        DrawTransition(spriteBatch);
    }

    #region Splash

    private static void DrawSplash(SpriteBatch spriteBatch)
    {
        CoolerMenuConfig config = CoolerMenuConfig.Instance;

        DynamicSpriteFont font = FontAssets.DeathText.Value;

        string splashText = CurrentSplash.Value;

        Vector2 textSize = font.MeasureString(splashText);

            // Position text under the logo.
        float xPos = config.LogoPosition == HorizontalPosition.Left ? 260f : Main.screenWidth - 260f;
        Vector2 textPosition = new(xPos, Main.screenHeight * .18f + 100f);

        textPosition.X -= textSize.X * .5f;

        Color splashColor = Main.OurFavoriteColor; // new(255, 255, 100);
        float splashScale = .6f;

        ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, splashText, textPosition, splashColor, 0f, Vector2.Zero, new(splashScale));
    }

    #endregion

    #region Backgrounds

    private static void DrawBackgrounds(SpriteBatch spriteBatch)
    {
        for (int i = 0; i < BackgroundAssets.Length; i++)
        {
            Texture2D texture = BackgroundAssets[i];

            float scale = (float)Main.screenWidth / texture.Width;
            float height = texture.Height * scale;

            Vector2 pos = new(0, Main.screenHeight - height);

            Color color = Color.White * BackgroundOpacities[i];

            spriteBatch.Draw(texture, pos, null, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }

    #endregion

    #region Buttons

    private static void DrawButton(
        SpriteBatch spriteBatch,
        Texture2D? icon,
        string text,
        Vector2 pos,
        float bobOffset,
        HorizontalPosition buttonPosition,
        ref bool hovered,
        Action clickAction)
    {
        CoolerMenuConfig config = CoolerMenuConfig.Instance;

        DynamicSpriteFont font = FontAssets.DeathText.Value;

        float baseScale = .7f;

        Vector2 textSizeVec = font.MeasureString(text) * baseScale;

        int buttonWidth = (int)textSizeVec.X;
        int buttonHeight = (int)textSizeVec.Y;

        float baseX =
            buttonPosition == HorizontalPosition.Left ?
            pos.X :
            Main.screenWidth - pos.X - buttonWidth;

        float x = baseX + (buttonPosition == HorizontalPosition.Left ? SlideOffset : -SlideOffset);

        bobOffset = config.EnableFloatingAnimation ? (float)Math.Sin(BobTimer + bobOffset) * 3f : 0f;

        Vector2 bobPos = new(x, pos.Y + bobOffset);

            // Offset text and draw an icon if applicable.
        Vector2 iconPos = Vector2.Zero;
        Vector2 textPos = bobPos;

        if (icon is not null)
        {
            iconPos = new(bobPos.X - IconOffset, bobPos.Y - 8f);
            textPos = new(bobPos.X + IconOffset - 20f, bobPos.Y);

            float iconScale = 1.6f;
            Color iconColor = Color.White;

            spriteBatch.Draw(icon, iconPos, null, iconColor, 0f, Vector2.Zero, iconScale, SpriteEffects.None, 0f);
        }

        Rectangle hitbox = new((int)bobPos.X, (int)bobPos.Y, buttonWidth, buttonHeight);

            // Hover sound.
        bool wasHovered = hovered;

        hovered = hitbox.Contains(Main.MouseScreen.ToPoint());

        if (hovered && !wasHovered)
            SoundEngine.PlaySound(SoundID.MenuTick);

        float scale = hovered ? baseScale * 1.1f : baseScale;
        Color color = hovered ? new Color(255, 230, 80) : Color.White;

            // Drop-shadow.
        ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, text, textPos + new Vector2(2, 2), Color.Black, 0f, Vector2.Zero, new Vector2(scale));
            // Main text.
        ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, text, textPos, color, 0f, Vector2.Zero, new Vector2(scale));

            // TODO: Make each button some data structure with a delegate for clicking.
        if (hovered && Main.mouseLeft && Main.mouseLeftRelease)
        {
            Main.mouseLeftRelease = false;
            SoundEngine.PlaySound(SoundID.MenuOpen);

            clickAction();
        }
    }

#nullable disable

    #endregion

    #region Transitioning

    public static void DrawTransition(SpriteBatch spriteBatch)
    {
        float eased = Easings.InOutCubic(SlideProgress);

        if (TransitionTarget is null || eased >= 1)
            return;
        
        CoolerMenuConfig config = CoolerMenuConfig.Instance;

            // This might be the FUNNIEST way ive seen an sb.HasBegun check be done. :sob:
            // Whats really funny is that this is only called by YOUR code, so you KNOW the state of the spritebatch. :sob:
                // try { Main.spriteBatch.End(); }
                // catch (Exception _) { isEnded = true; }

            // ApplyScaleMatrixOne(); - Stop.

        spriteBatch.End();
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Matrix.Identity);

        Vector2 position = Vector2.UnitX * eased * (config.ButtonPosition == HorizontalPosition.Left ? 200f : -200f);

        Color color = Color.White * MathF.Pow(1f - eased, 2f);

            // Why was this in a try-catch block??
        spriteBatch.Draw(TransitionTarget, position, color);

        spriteBatch.End();
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);

            // Main.DrawCursor(Main.DrawThickCursor()); - ?????????????
    }

    #endregion

    #endregion

    #region Temp Message

    private static void ShowTemporaryMessage(string message)
    {
        TempMessage = message;
        TempMessageTimer = 180f;
    }

    #endregion
}