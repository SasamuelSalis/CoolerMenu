using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.SymbolStore;
using System.Linq;
using System.Reflection;
using System.Runtime.Intrinsics.X86;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using Terraria.UI;
using Terraria.UI.Chat;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CoolerMenu
{
    // --- Config class ---
    public class CoolerMenuConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("Appearance")]
        [Label("Button Position")]
        [Tooltip("Which side of the screen the buttons appear on")]
        [DefaultValue(HorizontalPosition.Left)]
        public HorizontalPosition ButtonPosition;

        [Label("Logo Position")]
        [Tooltip("Which side of the screen the logo appears on")]
        [DefaultValue(HorizontalPosition.Left)]
        public HorizontalPosition LogoPosition;

        [Label("Hover Backgrounds")]
        [Tooltip("Show background images when hovering over buttons")]
        [DefaultValue(true)]
        public bool EnableHoverBackgrounds;

        [Header("Icons")]
        [Label("Button Icons")]
        [Tooltip("Show icons next to menu buttons")]
        [DefaultValue(true)]
        public bool EnableButtonIcons;

        [Label("Splash Messages")]
        [Tooltip("Show random splash messages under the logo")]
        [DefaultValue(true)]
        public bool EnableSplashMessages;

        [Header("Animations")]
        [Label("Floating Buttons")]
        [Tooltip("Enable floating/bobbing animation for buttons")]
        [DefaultValue(true)]
        public bool EnableFloatingAnimation;

        [Header("CoreMenuConfig")]
        [Label("Menu Active")]
        [Tooltip("Config way of toggling on and off the menu theme")]
        [DefaultValue(true)]
        public bool isMenuActive;
    }

    public enum HorizontalPosition { Left, Right }

    // --- ModMenu class ---
    public partial class CoolerMenu : ModSystem
    {
        private readonly Vector2[] buttonPositions = new Vector2[]
        {
            new Vector2(70f, 300f),
            new Vector2(70f, 360f),
            new Vector2(70f, 420f),
            new Vector2(70f, 480f),
            new Vector2(70f, 540f),
            new Vector2(70f, 600f)
        };

        private Texture2D singlePlayerIcon;
        private Texture2D multiPlayerIcon;
        private Texture2D settingsIcon;
        private Texture2D workshopIcon;
        private Texture2D achievementsIcon;

        private LocalizedText currentSplash = null;


        private int splashChangeTimer = 0;
        private const int splashChangeTime = 600;

        private LocalizedText GetRandomSplashMessage()
        {
            LocalizedText[] array = Language.FindAll(Lang.CreateDialogFilter("Mods.CoolerMenu.SplashMessages.Splash", null));
            if (array.Length > 0)
            {
                return array[Main.rand.Next(array.Length)];
            }
            return null;
        }



        private string[] GetButtonTexts()
        {
            return new string[]
            {
                Language.GetTextValue("Mods.CoolerMenu.SinglePlayer"),
                Language.GetTextValue("Mods.CoolerMenu.Multiplayer"),
                Language.GetTextValue("Mods.CoolerMenu.Achievements"),
                Language.GetTextValue("Mods.CoolerMenu.Settings"),
                Language.GetTextValue("Mods.CoolerMenu.Workshop"),
                Language.GetTextValue("Mods.CoolerMenu.Exit")
            };
        }

        private int currentHoveredButton = -1; // -1 means no button is hovered ok me?

        private float bobTimer = 0f;
        private float[] bobOffsets;
        private bool[] buttonHoverState;

        private string tempMessage = "";
        private float messageTimer = 0f;

        // Smoother slide animation
        private float slideOffset = -300f;
        private float slideProgress = 0f;
        private const float slideDuration = 80f;
        private bool shouldAnimateIn = false;
        private bool isTransitioning = false;
        private int transitionTimer = 0;
        private const int transitionDelay = 30;

        // --- Background images ---
        private Texture2D singlePlayerBg;
        private Texture2D multiPlayerBg;
        private Texture2D settingsBg;
        private Texture2D achievementsBg;
        private Texture2D workshopBg;
        private float singleBgAlpha = 0f;
        private float multiBgAlpha = 0f;
        private float settingsBgAlpha = 0f;
        private float achievementsBgAlpha = 0f;
        private float workshopBgAlpha = 0f;
        private const float fadeSpeed = 0.05f;

        // Track what action to perform after slide-out
        private Action delayedAction = null;

        // Add these new fields for better menu handling
        private int framesInMenu0 = 0;
        private const int minFramesBeforeSwitch = 2;

        private static bool isLoaded = false;

        public override void Load()
        {
            IL_MenuLoader.UpdateAndDrawModMenuInner += ModifyLogoPosition;
            IL_Main.DrawMenu += ModifyVanillaLogo;

            CoolerMenuUtils.Load();
            Load_Add();


        }

        public override void Unload()
        {
            //Unlike vanilla IL edits and detours, since this is a manual hook, we must unload it (tmodloader unloads any of its hooks automatically)
            IL_MenuLoader.UpdateAndDrawModMenuInner -= ModifyLogoPosition;

            CoolerMenuUtils.Unload();
        }

        private void ModifyLogoPosition(ILContext il)
        {
            ILCursor c = new(il);
            int logoPosition_locVar = -1;

            //Match to just before the calling of PreDrawLogo
            c.GotoNext(MoveType.Before, i => i.MatchLdsfld("Terraria.ModLoader.MenuLoader", "currentMenu"), i => i.MatchLdarg(0), i => i.MatchLdloca(out logoPosition_locVar), i => i.MatchLdarga(3), i => i.MatchLdloca(out _), i => i.MatchLdarga(2), i => i.MatchCallvirt<ModMenu>("PreDrawLogo"));

            //Emit a ref
            c.EmitLdloca(logoPosition_locVar);
            //Emit a delegate, editing the logo position through modifying the ref
            c.EmitDelegate((ref Vector2 logoDrawPos) =>
            {
                if (!CoolerMenuUtils.GetIsLoading())
                {
                    var config = ModContent.GetInstance<CoolerMenuConfig>();

                    if (!config.isMenuActive)
                        return;

                    float xPos = config.LogoPosition == HorizontalPosition.Left ? 260f : Main.screenWidth - 260f;
                    logoDrawPos = new Vector2(xPos, Main.screenHeight * 0.18f);

                    float logoScale = Main.instance.GetLogoScale();
                    logoScale = 1.2f;
                    Main.instance.SetLogoScale(logoScale);

                    UpdateMenu();
                }
            });

            //Goto past the post draw
            c.GotoNext(MoveType.After, i => i.MatchCallvirt<ModMenu>("PostDrawLogo"));

            //Render extra bits such as the toggles and the buttons
            c.EmitDelegate(() =>
            {
                if (!CoolerMenuUtils.GetIsLoading())
                {
                    RenderVanillaMenuToggle();
                    RenderCoreMenuToggle();
                    PostDrawMenu(Main.spriteBatch);
                }
            });
        }

        private void ModifyVanillaLogo(ILContext il)
        {
            ILCursor c = new(il);

            //Draws the logo 4 times, we repeat the edit 4 times
            for (int j = 0; j < 4; j++)
            {
                //Move to after the positioning of the draw
                c.GotoNext(MoveType.After, i => i.MatchLdsfld<Main>("screenWidth"), i => i.MatchLdcI4(2), i => i.MatchDiv(), i => i.MatchConvR4(), i => i.MatchLdcR4(100), i => i.MatchNewobj<Vector2>(".ctor"));
                //Modify the position (get old pos, return new pos)
                c.EmitDelegate((Vector2 currentLogoPos) =>
                {
                    if (!CoolerMenuUtils.GetIsLoading())
                    {
                        var config = ModContent.GetInstance<CoolerMenuConfig>();
                        
                            if (!config.isMenuActive)
                            return currentLogoPos;

                        float xPos = config.LogoPosition == HorizontalPosition.Left ? 260f : Main.screenWidth - 260f;
                        return new Vector2(xPos, Main.screenHeight * 0.18f);
                    }
                    return currentLogoPos;
                });
            }
        }

        public void OnSelected()
        {
            Main.menuMode = 0;
            bobOffsets = new float[buttonPositions.Length];
            buttonHoverState = new bool[buttonPositions.Length];
            currentHoveredButton = -1; // Reset hover state

            for (int i = 0; i < bobOffsets.Length; i++)
                bobOffsets[i] = (float)(Main.rand.NextDouble() * Math.PI * 2);

            // Start fresh
            slideOffset = -300f;
            slideProgress = 0f;
            shouldAnimateIn = true;
            isTransitioning = false;
            transitionTimer = 0;
            framesInMenu0 = 0;
            delayedAction = null;

            isLoaded = true;

            LoadTextures();
        }

        private void LoadTextures()
        {
            if (!Main.dedServ)
            {
                try
                {
                    singlePlayerBg = ModContent.Request<Texture2D>("CoolerMenu/Assets/SinglePlayer", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
                    multiPlayerBg = ModContent.Request<Texture2D>("CoolerMenu/Assets/MultiPlayer", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
                    settingsBg = ModContent.Request<Texture2D>("CoolerMenu/Assets/Settings", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
                    achievementsBg = ModContent.Request<Texture2D>("CoolerMenu/Assets/Achievements", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
                    workshopBg = ModContent.Request<Texture2D>("CoolerMenu/Assets/Workshop", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;

                    // Load icons
                    singlePlayerIcon = ModContent.Request<Texture2D>("CoolerMenu/Assets/SinglePlayerIcon", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
                    multiPlayerIcon = ModContent.Request<Texture2D>("CoolerMenu/Assets/MultiPlayerIcon", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
                    settingsIcon = ModContent.Request<Texture2D>("CoolerMenu/Assets/SettingsIcon", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
                    workshopIcon = ModContent.Request<Texture2D>("CoolerMenu/Assets/WorkshopIcon", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
                    achievementsIcon = ModContent.Request<Texture2D>("CoolerMenu/Assets/AchievementsIcon", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load menu textures: {e.Message}");
                }
            }
        }

        // Smooth easing function
        private float EaseInOut(float t)
        {
            return t < 0.5f ? 4 * t * t * t : 1 - (float)Math.Pow(-2 * t + 2, 3) / 2;
        }

        public void UpdateMenu()
        {
            var config = ModContent.GetInstance<CoolerMenuConfig>();

            if (!config.isMenuActive)
                return;

            if (!isLoaded)
                OnSelected();

            // Auto-switch to custom menu mode
            if (Main.menuMode == 0)
            {
                framesInMenu0++;
                if (framesInMenu0 >= minFramesBeforeSwitch && !isTransitioning)
                {
                    Main.menuMode = 1007;
                    framesInMenu0 = 0;
                }
            }
            else
            {
                framesInMenu0 = 0;
            }

            // Handle transitions
            if (isTransitioning)
            {
                transitionTimer++;

                if (transitionTimer < transitionDelay)
                {
                    // Slide out animation
                    slideProgress = Math.Max(slideProgress - (1f / (slideDuration * 0.5f)), 0f);
                    float eased = EaseInOut(slideProgress);
                    slideOffset = -300f + (eased * 300f);
                }
                else if (transitionTimer >= transitionDelay)
                {
                    // Complete transition - execute the delayed action
                    isTransitioning = false;
                    transitionTimer = 0;
                    delayedAction?.Invoke();
                    delayedAction = null;
                    return;
                }
            }

            // Handle normal slide animation
            if (!isTransitioning)
            {
                if (shouldAnimateIn && slideProgress < 1f)
                {
                    // Slide in animation
                    slideProgress += 1f / slideDuration * 1.5f;
                    slideProgress = Math.Min(slideProgress, 1f);
                    float eased = EaseInOut(slideProgress);
                    slideOffset = -300f + (eased * 300f);
                }
                else if (!shouldAnimateIn && slideProgress > 0f)
                {
                    // Slide out animation (for immediate transitions)
                    slideProgress -= 1f / (slideDuration * 1f);
                    slideProgress = Math.Max(slideProgress, 0f);
                    float eased = EaseInOut(slideProgress);
                    slideOffset = -300f + (eased * 300f);
                }
            }

            // Message timer
            if (messageTimer > 0)
                messageTimer--;

            // Bob animation
            bobTimer += 0.05f;

            // Update splash message timer - ONLY IF ENABLED
            if (config.EnableSplashMessages)
            {
                splashChangeTimer++;
                if (splashChangeTimer >= splashChangeTime)
                {
                    currentSplash = GetRandomSplashMessage();
                    splashChangeTimer = 0;
                }

                // Initialize first splash message
                currentSplash ??= GetRandomSplashMessage();

                // When drawing, use the LocalizedText's Value property
                if (config.EnableSplashMessages && currentSplash != null)
                {
                    string splashText = currentSplash.Value; // This gets the current localized text
                                                             // Use splashText for drawing...
                }
            }
            else
            {
                // Clear splash message if disabled
                currentSplash = null;
            }

            // Update hover backgrounds
            if (config.EnableHoverBackgrounds)
            {
                // Reset all alphas first
                singleBgAlpha = Math.Max(singleBgAlpha - fadeSpeed, 0f);
                multiBgAlpha = Math.Max(multiBgAlpha - fadeSpeed, 0f);
                settingsBgAlpha = Math.Max(settingsBgAlpha - fadeSpeed, 0f);
                achievementsBgAlpha = Math.Max(achievementsBgAlpha - fadeSpeed, 0f);
                workshopBgAlpha = Math.Max(workshopBgAlpha - fadeSpeed, 0f);

                // Check for new hover
                int newlyHovered = -1;
                for (int i = 0; i < buttonPositions.Length; i++)
                {
                    if (GetButtonHitbox(i).Contains(Main.MouseScreen.ToPoint()))
                    {
                        newlyHovered = i;
                        break;
                    }
                }

                // If hovering a new button, update current hover
                if (newlyHovered != -1 && newlyHovered != currentHoveredButton)
                {
                    currentHoveredButton = newlyHovered;
                }

                // If not hovering any button, keep the last background visible but fading
                if (newlyHovered == -1 && currentHoveredButton != -1)
                {
                    // Slowly fade out the current background instead of immediately removing it
                    switch (currentHoveredButton)
                    {
                        case 0: singleBgAlpha = Math.Max(singleBgAlpha - fadeSpeed * 0.3f, 0.3f); break;
                        case 1: multiBgAlpha = Math.Max(multiBgAlpha - fadeSpeed * 0.3f, 0.3f); break;
                        case 2: achievementsBgAlpha = Math.Max(achievementsBgAlpha - fadeSpeed * 0.3f, 0.3f); break;
                        case 3: settingsBgAlpha = Math.Max(settingsBgAlpha - fadeSpeed * 0.3f, 0.3f); break;
                        case 4: workshopBgAlpha = Math.Max(workshopBgAlpha - fadeSpeed * 0.3f, 0.3f); break;
                    }
                }
                // If hovering a button, make that background fully visible
                else if (newlyHovered != -1)
                {
                    switch (newlyHovered)
                    {
                        case 0: singleBgAlpha = Math.Min(singleBgAlpha + fadeSpeed * 2f, 0.9f); break;
                        case 1: multiBgAlpha = Math.Min(multiBgAlpha + fadeSpeed * 2f, 0.9f); break;
                        case 2: achievementsBgAlpha = Math.Min(achievementsBgAlpha + fadeSpeed * 2f, 0.9f); break;
                        case 3: settingsBgAlpha = Math.Min(settingsBgAlpha + fadeSpeed * 2f, 0.9f); break;
                        case 4: workshopBgAlpha = Math.Min(workshopBgAlpha + fadeSpeed * 2f, 0.9f); break;
                    }
                }
                // If no button has ever been hovered, reset completely
                else if (currentHoveredButton == -1)
                {
                    singleBgAlpha = 0f;
                    multiBgAlpha = 0f;
                    settingsBgAlpha = 0f;
                    achievementsBgAlpha = 0f;
                    workshopBgAlpha = 0f;
                }
            }

            // Slide in when in custom menu mode
            bool shouldShowButtons = (Main.menuMode == 1007) && !shouldAnimateIn && !isTransitioning;
            if (shouldShowButtons)
            {
                shouldAnimateIn = true;
                slideProgress = 0f;
            }
        }

        private void RenderVanillaMenuToggle()
        {
            if (Main.menuMode != 0 && Main.menuMode != 1007)
                return;

            var config = ModContent.GetInstance<CoolerMenuConfig>();

            if (!config.isMenuActive)
                return;

            //Re-render and re-implement the menu switcher
            ModMenu currentMenu = CoolerMenuUtils.GetCurrentMenu();
            List<ModMenu> menus = CoolerMenuUtils.GetMenus();
            bool notifyNewMainMenuThemes = CoolerMenuUtils.GetNotifyNewMainMenuThemes();
            int newMenus;
            if (menus != null)
            {
                lock (menus)
                {
                    newMenus = menus.Count((ModMenu m) => m.IsNew);
                }
                byte b = (byte)((255 + Main.tileColor.R * 2) / 3);
                Color color = new(b, b, b, 255);
                string text = Language.GetTextValue("tModLoader.ModMenuSwap") + ": " + currentMenu.DisplayName + ((newMenus == 0) ? "" : (notifyNewMainMenuThemes ? $" ({newMenus} New)" : ""));
                Vector2 menuTextSize = ChatManager.GetStringSize(FontAssets.MouseText.Value, ChatManager.ParseMessage(text, color).ToArray(), Vector2.One);
                Rectangle switchTextRect = new Rectangle((int)((float)(Main.screenWidth / 2) - menuTextSize.X / 2f), (int)((float)(Main.screenHeight - 2) - menuTextSize.Y), (int)menuTextSize.X, (int)menuTextSize.Y);
                if (switchTextRect.Contains(Main.mouseX, Main.mouseY) && !Main.alreadyGrabbingSunOrMoon)
                {
                    if (Main.mouseLeftRelease && Main.mouseLeft)
                    {
                        SoundEngine.PlaySound(in SoundID.MenuTick);
                        CoolerMenuUtils.OffsetModMenu(1);
                    }
                    else if (Main.mouseRightRelease && Main.mouseRight)
                    {
                        SoundEngine.PlaySound(in SoundID.MenuTick);
                        CoolerMenuUtils.OffsetModMenu(-1);
                    }
                }
                ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value, text, new Vector2((float)switchTextRect.X, (float)switchTextRect.Y), (switchTextRect.Contains(Main.mouseX, Main.mouseY) ? Main.OurFavoriteColor : new Color(120, 120, 120, 76)), 0f, Vector2.Zero, Vector2.One);
            }
        }

        private void RenderCoreMenuToggle()
        {
            if (Main.menuMode != 0 && Main.menuMode != 1007)
                return;

            var config = ModContent.GetInstance<CoolerMenuConfig>();
            byte b = (byte)((255 + Main.tileColor.R * 2) / 3);
            Color color = new(b, b, b, 255);
            string text = Language.GetTextValue("Mods.CoolerMenu.CoreMenu.CoreMenuSwap") + (config.isMenuActive ? Language.GetTextValue("Mods.CoolerMenu.CoreMenu.CoolerMenu") : Language.GetTextValue("Mods.CoolerMenu.CoreMenu.Vanilla"));
            Vector2 menuTextSize = ChatManager.GetStringSize(FontAssets.MouseText.Value, ChatManager.ParseMessage(text, color).ToArray(), Vector2.One);
            Rectangle switchTextRect = new Rectangle((int)((float)(Main.screenWidth / 2) - menuTextSize.X / 2f), (int)((float)(Main.screenHeight - 2) - (menuTextSize.Y * 2f)), (int)menuTextSize.X, (int)menuTextSize.Y);
            if (switchTextRect.Contains(Main.mouseX, Main.mouseY) && !Main.alreadyGrabbingSunOrMoon)
            {
                if ((Main.mouseLeftRelease && Main.mouseLeft) || (Main.mouseRightRelease && Main.mouseRight))
                {
                    SoundEngine.PlaySound(in SoundID.MenuTick);
                    config.isMenuActive = !config.isMenuActive;
                    config.SaveChanges();
                    isLoaded = false;
                    if (!config.isMenuActive && Main.menuMode == 1007)
                        Main.menuMode = 0;

                }
            }
            ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value, text, new Vector2((float)switchTextRect.X, (float)switchTextRect.Y), (switchTextRect.Contains(Main.mouseX, Main.mouseY) ? Main.OurFavoriteColor : new Color(120, 120, 120, 76)), 0f, Vector2.Zero, Vector2.One);
        }

        public void PostDrawMenu(SpriteBatch spriteBatch)
        {
            if (Main.menuMode != 0 && Main.menuMode != 1007)
                return;

            var config = ModContent.GetInstance<CoolerMenuConfig>();

            if (!config.isMenuActive)
                return;

            // DRAW SPLASH MESSAGE UNDER LOGO (Minecraft style) - ONLY IF ENABLED
            if (config.EnableSplashMessages && currentSplash != null)
            {
                string splashText = currentSplash.Value; // Convert to string here
                var font = FontAssets.MouseText.Value;
                Vector2 textSize = font.MeasureString(splashText); // Use the string variable

                // Position text under the logo - adjust these values as needed
                float xPos = config.LogoPosition == HorizontalPosition.Left ? 260f : Main.screenWidth - 260f;
                Vector2 textPosition = new Vector2(xPos, Main.screenHeight * 0.18f + 100f);

                // Center the text
                textPosition.X -= textSize.X * 0.5f;

                // Minecraft-style yellow color with shadow
                Color splashColor = new Color(255, 255, 100);
                float splashScale = 1.1f;

                // Draw with shadow - use splashText here too
                ChatManager.DrawColorCodedStringWithShadow(
                    spriteBatch,
                    font,
                    splashText, // Use the string variable
                    textPosition,
                    splashColor,
                    0f,
                    Vector2.Zero,
                    new Vector2(splashScale)
                );
            }

            if ((singlePlayerBg == null || multiPlayerBg == null || settingsBg == null) && !Main.dedServ)
            {
                LoadTextures();
            }

            // Draw hover backgrounds
            if (config.EnableHoverBackgrounds && singlePlayerBg != null && multiPlayerBg != null && settingsBg != null)
            {
                if (singleBgAlpha > 0f) DrawBackground(spriteBatch, singlePlayerBg, singleBgAlpha);
                if (multiBgAlpha > 0f) DrawBackground(spriteBatch, multiPlayerBg, multiBgAlpha);
                if (settingsBgAlpha > 0f) DrawBackground(spriteBatch, settingsBg, settingsBgAlpha);
                if (achievementsBgAlpha > 0f) DrawBackground(spriteBatch, achievementsBg, achievementsBgAlpha);
                if (workshopBgAlpha > 0f) DrawBackground(spriteBatch, workshopBg, workshopBgAlpha);
            }

            // Draw buttons if they're visible
            if (slideOffset > -250f)
            {
                var buttonTextsLocalized = GetButtonTexts();
                for (int i = 0; i < buttonPositions.Length; i++)
                    DrawFloatingButton(spriteBatch, buttonTextsLocalized[i], buttonPositions[i], i, config.ButtonPosition);
            }

            // Draw temporary message
            if (messageTimer > 0)
            {
                var font = FontAssets.MouseText.Value;
                Vector2 size = font.MeasureString(tempMessage) * 1.5f;
                Vector2 pos = new Vector2(Main.screenWidth / 2 - size.X / 2, Main.screenHeight - 100);
                ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, tempMessage, pos, Color.Yellow, 0f, Vector2.Zero, new Vector2(1.5f));
            }
            PostDrawMenu_Add();
        }

        private void DrawBackground(SpriteBatch spriteBatch, Texture2D tex, float alpha)
        {
            if (tex == null || tex.IsDisposed) return;

            float scale = (float)Main.screenWidth / tex.Width;
            float height = tex.Height * scale;
            Vector2 pos = new Vector2(0, Main.screenHeight - height);
            spriteBatch.Draw(tex, pos, null, Color.White * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private Rectangle GetButtonHitbox(int index)
        {
            var config = ModContent.GetInstance<CoolerMenuConfig>();
            var buttonTextsLocalized = GetButtonTexts();
            var font = FontAssets.MouseText.Value;
            float baseScale = 1.5f;
            Vector2 textSizeVec = font.MeasureString(buttonTextsLocalized[index]) * baseScale;
            int width = (int)textSizeVec.X;
            int height = (int)textSizeVec.Y;

            float baseX = config.ButtonPosition == HorizontalPosition.Left ?
                buttonPositions[index].X :
                Main.screenWidth - buttonPositions[index].X - width;

            float x = baseX + (config.ButtonPosition == HorizontalPosition.Left ? slideOffset : -slideOffset);

            // Only apply bob offset if floating animation is enabled
            float bobOffset = config.EnableFloatingAnimation ? (float)Math.Sin(bobTimer + bobOffsets[index]) * 3f : 0f;

            return new Rectangle((int)x, (int)(buttonPositions[index].Y + bobOffset), width, height);
        }

        private void DrawFloatingButton(SpriteBatch spriteBatch, string text, Vector2 pos, int index, HorizontalPosition buttonPosition)
        {
            var config = ModContent.GetInstance<CoolerMenuConfig>();
            var font = FontAssets.MouseText.Value;
            float baseScale = 1.5f;
            Vector2 textSizeVec = font.MeasureString(text) * baseScale;
            int buttonWidth = (int)textSizeVec.X;
            int buttonHeight = (int)textSizeVec.Y;

            float baseX = buttonPosition == HorizontalPosition.Left ?
                pos.X :
                Main.screenWidth - pos.X - buttonWidth;

            float x = baseX + (buttonPosition == HorizontalPosition.Left ? slideOffset : -slideOffset);
            float bobOffset = config.EnableFloatingAnimation ? (float)Math.Sin(bobTimer + bobOffsets[index]) * 3f : 0f;
            Vector2 bobPos = new Vector2(x, pos.Y + bobOffset);

            // Calculate icon position for buttons that have icons
            Vector2 iconPos = Vector2.Zero;
            Vector2 textPos = bobPos;

            if (config.EnableButtonIcons)
            {
                float iconOffset = 30f; // Space between icon and text

                if (index == 0 && singlePlayerIcon != null) // Single Player button
                {
                    iconPos = new Vector2(bobPos.X - iconOffset, bobPos.Y - 10f);
                    textPos = new Vector2(bobPos.X + iconOffset - 20f, bobPos.Y);
                }
                else if (index == 1 && multiPlayerIcon != null) // Multiplayer button
                {
                    iconPos = new Vector2(bobPos.X - iconOffset, bobPos.Y - 10f);
                    textPos = new Vector2(bobPos.X + iconOffset - 20f, bobPos.Y);
                }
                else if (index == 2 && achievementsIcon != null) // Achievements button
                {
                    iconPos = new Vector2(bobPos.X - iconOffset, bobPos.Y - 10f);
                    textPos = new Vector2(bobPos.X + iconOffset - 20f, bobPos.Y);
                }
                else if (index == 3 && settingsIcon != null) // Settings button
                {
                    iconPos = new Vector2(bobPos.X - iconOffset, bobPos.Y - 5f);
                    textPos = new Vector2(bobPos.X + iconOffset - 20f, bobPos.Y);
                }
                else if (index == 4 && workshopIcon != null) // Workshop button
                {
                    iconPos = new Vector2(bobPos.X - iconOffset, bobPos.Y - 5f);
                    textPos = new Vector2(bobPos.X + iconOffset - 20f, bobPos.Y);
                }
            }

            Rectangle hitbox = new Rectangle((int)bobPos.X, (int)bobPos.Y, buttonWidth, buttonHeight);

            // Hover sound on state change
            bool hovered = hitbox.Contains(Main.MouseScreen.ToPoint());
            if (hovered && !buttonHoverState[index])
                SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);
            buttonHoverState[index] = hovered;

            float scale = hovered ? baseScale * 1.1f : baseScale;
            Color color = hovered ? new Color(255, 230, 80) : Color.White;

            // Draw icons for buttons that have them
            if (config.EnableButtonIcons)
            {
                float iconScale = 1.6f;
                Color iconColor = Color.White;

                if (index == 0 && singlePlayerIcon != null) // Single Player
                {
                    spriteBatch.Draw(singlePlayerIcon, iconPos, null, iconColor, 0f, Vector2.Zero, iconScale, SpriteEffects.None, 0f);
                }
                else if (index == 1 && multiPlayerIcon != null) // Multiplayer
                {
                    spriteBatch.Draw(multiPlayerIcon, iconPos, null, iconColor, 0f, Vector2.Zero, iconScale, SpriteEffects.None, 0f);
                }
                else if (index == 2 && achievementsIcon != null) // Achievements - ADDED THIS
                {
                    spriteBatch.Draw(achievementsIcon, iconPos, null, iconColor, 0f, Vector2.Zero, iconScale, SpriteEffects.None, 0f);
                }
                else if (index == 3 && settingsIcon != null) // Settings
                {
                    spriteBatch.Draw(settingsIcon, iconPos, null, iconColor, 0f, Vector2.Zero, iconScale, SpriteEffects.None, 0f);
                }
                else if (index == 4 && workshopIcon != null) // Workshop
                {
                    spriteBatch.Draw(workshopIcon, iconPos, null, iconColor, 0f, Vector2.Zero, iconScale, SpriteEffects.None, 0f);
                }
            }

            // Shadow
            ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, text, textPos + new Vector2(2, 2), Color.Black, 0f, Vector2.Zero, new Vector2(scale));
            // Main text
            ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, text, textPos, color, 0f, Vector2.Zero, new Vector2(scale));

            // Button click action
            if (hovered && Main.mouseLeft && Main.mouseLeftRelease)
            {
                Main.mouseLeftRelease = false;
                SoundEngine.PlaySound(Terraria.ID.SoundID.MenuOpen);

                switch (index)
                {
                    case 0: // Single Player
                        StartTransition(() => Main.menuMode = 1);
                        break;
                    case 1: // Multiplayer
                        StartTransition(() => Main.menuMode = 12);
                        break;
                    case 2: // Achievements
                        StartTransition(OpenAchievements);
                        break;
                    case 3: // Settings
                        StartTransition(() => Main.menuMode = 11);
                        break;
                    case 4: // Workshop
                        StartTransition(OpenWorkshop);
                        break;
                    case 5: // Exit
                        Main.instance.Exit();
                        break;
                }
            }
        }

        private void OpenWorkshop()
        {
            try
            {
                SoundEngine.PlaySound(SoundID.MenuTick);
                Main.menuMode = 888;

                UIWorkshopHub workshopHub = new UIWorkshopHub(null);
                workshopHub.EnterHub();
                Main.MenuUI.SetState(workshopHub);
            }
            catch (Exception ex)
            {
                ShowTemporaryMessage("Failed to open Workshop");
            }
        }

        private void OpenAchievements()
        {
            try
            {
                SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);
                Main.menuMode = 888;
                Main.MenuUI.SetState(CoolerMenuUtils.GetAchievementsMenu());
            }
            catch (Exception ex)
            {
                ShowTemporaryMessage("Failed to open Achievements");
            }
        }

        private void StartTransition(Action action)
        {
            isTransitioning = true;
            transitionTimer = 0;
            delayedAction = action;
            shouldAnimateIn = false;
            StartTransition_Add();

        }

        private void ShowTemporaryMessage(string message)
        {
            tempMessage = message;
            messageTimer = 180f;
        }
    }

    // CoolerMenuTransitions thingy

    public partial class CoolerMenu : ModSystem
    {
        private static CoolerMenu Instance;
        private static GraphicsDevice GraphicsDevice => Main.instance.GraphicsDevice;
        #region Render Target
        private static RenderTarget2D TransitionRenderTarget;
        private static Point TransitionRenderTargetSize;
        private static void UpdateTransitionRenderTarget()
        {
            Point size = new Point(
                GraphicsDevice.PresentationParameters.BackBufferWidth,
                GraphicsDevice.PresentationParameters.BackBufferHeight
            );
            if (TransitionRenderTargetSize != size || TransitionRenderTarget == null)
            {
                if (TransitionRenderTarget != null)
                {
                    TransitionRenderTarget.Dispose(); // Clearing memory
                }
                TransitionRenderTarget = new RenderTarget2D(GraphicsDevice, size.X, size.Y, mipMap: false, GraphicsDevice.PresentationParameters.BackBufferFormat, DepthFormat.None, GraphicsDevice.PresentationParameters.MultiSampleCount, RenderTargetUsage.PreserveContents);
                TransitionRenderTargetSize = size;
            }
        }
        #endregion

        #region Helpers
        private static UIElement ApplyScaleMatrixElement = new()
        {
            OverrideSamplerState = Main.DefaultSamplerState  // Force "Begin" in spriteBatch with updated scaled matrix
        };
        private static void ApplyScaleMatrix()
        {
            ApplyScaleMatrixElement.Draw(Main.spriteBatch);
        }
        private static void ApplyScaleMatrixOne()
        {
            var lastUIScale = Main.UIScale;
            Main.UIScale = 1f;
            ApplyScaleMatrix();
            Main.UIScale = lastUIScale;
        }
        #endregion

        public void Load_Add()
        {
            Instance = this;
            On_UserInterface.Draw += On_UserInterface_Draw;
        }
        public void StartTransition_Add()
        {
            delayedAction();
            delayedAction = null;
            if (Main.menuMode == 11 || Main.menuMode == 12)  // If state is hardcoded, then clearing cached image
            {
                TransitionRenderTarget = null;
            }
        }
        private static bool IsUIPostDrawMenu = false;
        public void PostDrawMenu_Add()
        {
            if (IsUIPostDrawMenu) return;
            var size = new Point(
                GraphicsDevice.PresentationParameters.BackBufferWidth,
                GraphicsDevice.PresentationParameters.BackBufferHeight
            );
            // If cached image is null, then does not render cached image
            //
            if (TransitionRenderTarget == null || TransitionRenderTargetSize != size) return;

            var eased = EaseInOut(slideProgress);

            // Do not draw if image is already fully transparent
            if (eased < 1)
            {
                bool isEnded = false;
                try { Main.spriteBatch.End(); }
                catch (Exception _) { isEnded = true; }
                Main.spriteBatch.Begin();

                ApplyScaleMatrixOne();

                Main.spriteBatch.Draw(
                    TransitionRenderTarget,
                    Vector2.UnitX * eased * (ModContent.GetInstance<CoolerMenuConfig>().ButtonPosition == HorizontalPosition.Left ? 200f : -200f),
                    null,
                    Color.White * MathF.Pow(1f - eased, 2f)
                );

                Terraria.GameInput.PlayerInput.SetZoom_UI();
                ApplyScaleMatrix();

                Main.DrawCursor(Main.DrawThickCursor());

                if (isEnded) Main.spriteBatch.End();
            }
        }
        private static void On_UserInterface_Draw(On_UserInterface.orig_Draw orig, UserInterface self, SpriteBatch spriteBatch, GameTime time)
        {
            var config = ModContent.GetInstance<CoolerMenuConfig>();
            if (self != Main.MenuUI || !config.isMenuActive || Main.menuMode == 0 || Main.menuMode == 1007)

            {
                orig(self, spriteBatch, time);
                return;
            }
            UpdateTransitionRenderTarget();

            var eased = Instance.EaseInOut(Instance.slideProgress);

            if (Instance.isTransitioning)
            {
                try { Instance.UpdateMenu(); }
                catch (Exception _) { }
            }

            if (TransitionRenderTarget == null)
            {
                orig(self, spriteBatch, time);
                return;
            }

            PlayerInput.SetZoom_UI();
            ApplyScaleMatrix();

            var lastMenuMode = Main.menuMode;
            Main.menuMode = 0;
            IsUIPostDrawMenu = true;
            try { Instance.PostDrawMenu(spriteBatch); }
            catch (Exception _) { }
            IsUIPostDrawMenu = false;
            Main.menuMode = lastMenuMode;

            spriteBatch.End();

            // Used to not clear back buffer when setting new render target
            //
            var lastRenderTargetUsage = GraphicsDevice.PresentationParameters.RenderTargetUsage;
            GraphicsDevice.PresentationParameters.RenderTargetUsage = RenderTargetUsage.PreserveContents;

            // Remembering last renderTargets
            //
            var lastRenderTargets = GraphicsDevice.GetRenderTargets();


            GraphicsDevice.SetRenderTarget(TransitionRenderTarget);
            GraphicsDevice.Clear(ClearOptions.Target, Color.Transparent, 0, 0);


            spriteBatch.Begin();
            ApplyScaleMatrix();


            orig(self, spriteBatch, time);


            spriteBatch.End();
            GraphicsDevice.SetRenderTargets(lastRenderTargets); // Restoring renderTargets
            spriteBatch.Begin();


            ApplyScaleMatrixOne();

            // Drawing Render target with transition and alpha
            //
            try
            {
                spriteBatch.Draw(
                    TransitionRenderTarget,
                    Vector2.UnitX * eased * (config.ButtonPosition == HorizontalPosition.Left ? 200f : -200f),
                    null,
                    Color.White * MathF.Sqrt(1f - eased)
                );
            }
            catch (Exception _) { }

            // Restoring changed parameters
            //
            GraphicsDevice.PresentationParameters.RenderTargetUsage = lastRenderTargetUsage;


            PlayerInput.SetZoom_UI();
            ApplyScaleMatrix();
        }
    }

    //Used to reflect only once to save on time ingame
    internal static class CoolerMenuUtils
    {
        internal static void Load()
        {
            _addcurrentMenu = typeof(MenuLoader).GetField("currentMenu", BindingFlags.NonPublic | BindingFlags.Static);
            _addmenus = typeof(MenuLoader).GetField("menus", BindingFlags.NonPublic | BindingFlags.Static);
            _addAchievementsMenu = typeof(Main).GetField("AchievementsMenu", BindingFlags.Public | BindingFlags.Static);
            _addOffsetModMenu = typeof(MenuLoader).GetMethod("OffsetModMenu", BindingFlags.Static | BindingFlags.NonPublic);
            _addlogoScale = typeof(Main).GetField("logoScale", BindingFlags.Instance | BindingFlags.NonPublic);
            _addnotifyNewMainMenuThemes = typeof(ModLoader).GetField("notifyNewMainMenuThemes", BindingFlags.Static | BindingFlags.NonPublic);
            _addisLoading = typeof(ModLoader).GetField("isLoading", BindingFlags.Static | BindingFlags.NonPublic);

        }

        internal static void Unload()
        {
            _addcurrentMenu = null;
            _addmenus = null;
            _addAchievementsMenu = null;
            _addOffsetModMenu = null;
            _addlogoScale = null;
            _addnotifyNewMainMenuThemes = null;
            _addisLoading = null;
        }

        internal static FieldInfo _addcurrentMenu;
        internal static FieldInfo _addmenus;
        internal static FieldInfo _addAchievementsMenu;
        internal static MethodInfo _addOffsetModMenu;
        internal static FieldInfo _addlogoScale;
        internal static FieldInfo _addnotifyNewMainMenuThemes;
        internal static FieldInfo _addisLoading;

        internal static ModMenu GetCurrentMenu()
        {
            if (_addcurrentMenu != null && _addcurrentMenu.GetValue(null) is ModMenu _value)
            {
                return _value;
            }
            return null;
        }

        internal static List<ModMenu> GetMenus()
        {
            if (_addmenus != null && _addmenus.GetValue(null) is List<ModMenu> _value)
            {
                return _value;
            }
            return null;
        }

        internal static UIState GetAchievementsMenu()
        {
            if (_addAchievementsMenu != null && _addAchievementsMenu.GetValue(null) is UIAchievementsMenu _value)
            {
                return _value as UIState;
            }
            return null;
        }

        internal static void OffsetModMenu(int indexForwardBy)
        {
            if (_addOffsetModMenu != null)
            {
                _addOffsetModMenu.Invoke(null, [indexForwardBy]);
            }
        }

        internal static bool GetNotifyNewMainMenuThemes()
        {
            if (_addnotifyNewMainMenuThemes != null && _addnotifyNewMainMenuThemes.GetValue(null) is bool _value)
            {
                return _value;
            }
            return false;
        }

        internal static bool GetIsLoading()
        {
            if (_addisLoading != null && _addisLoading.GetValue(null) is bool _value)
            {
                return _value;
            }
            return false;
        }

        internal static float GetLogoScale(this Main self)
        {
            if (_addlogoScale != null && _addlogoScale.GetValue(self) is float _value)
            {
                return _value;
            }
            return 0f;
        }

        internal static void SetLogoScale(this Main self, float scale)
        {
            if (_addlogoScale != null && _addlogoScale.GetValue(self) is float)
            {
                _addlogoScale.SetValue(self, scale);
            }
        }
    }
}