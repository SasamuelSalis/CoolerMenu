using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System.ComponentModel;
using System.Reflection;
using Terraria.ModLoader;

namespace CoolerMenu.Common.Menu;

    // Due to Terraria.ModLoader being protected, we need to manually create a hook-able class to modify the contents of UpdateAndDrawModMenuInner
public static class IL_MenuLoader
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static ILHook ILHook_UpdateAndDrawModMenuInner = null;

    public static event ILContext.Manipulator UpdateAndDrawModMenuInner
    {
        add
        {
            ILHook_UpdateAndDrawModMenuInner = new ILHook(typeof(MenuLoader).GetMethod(nameof(MenuLoader.UpdateAndDrawModMenuInner), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public | BindingFlags.Instance), value);
            
            ILHook_UpdateAndDrawModMenuInner?.Apply();
        }
        remove =>
            ILHook_UpdateAndDrawModMenuInner?.Dispose();
    }
}
