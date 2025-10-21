using Terraria;
using Terraria.ModLoader;

namespace CoolerMenu
{
    public class MenuSystem : ModSystem
    {
        public static bool WasInMainMenu = false;
        public static int PreviousMenu = 0;

        public override void PreUpdateEntities()
        {
            // Track when we leave main menu
            if (Main.menuMode == 0 || Main.menuMode == 888)
            {
                WasInMainMenu = true;
            }
            else if (WasInMainMenu)
            {
                // We just left main menu, store the previous state
                WasInMainMenu = false;
            }

            PreviousMenu = Main.menuMode;
        }
    }
}