using RecompOne.Runtime;
using RecompOne.Runtime.Memory;
using Recompiled;

// Entry point for the Tecmo's Deception (SLUS-00340) port.
// Before the runtime reads settings.json or the memory cards.
// The window icon is the same picture embedded in the executable. Set before
// the window exists so the first frame already wears it; when the launcher has
// already opened the window, this still replaces whatever it set.
Runtime.AppId = "deception";
var iconPath = Path.Combine(AppContext.BaseDirectory, "deception.png");
if (File.Exists(iconPath))
    Runtime.SetIcon(File.ReadAllBytes(iconPath));

Deception.LegacyData.Migrate();
Deception.GraphicsOptions.Install();
Deception.PrimHeap.Install();
// 8 MB. The two primitive buffers are parked above the console's 2 MB, and a
// 2 MB guest would alias those stores back onto the start of RAM.
var memory = new PSMemory(MemoryMap.DevkitRamSize);
try
{
    Entry.Run(memory, args.Length > 0 ? args[0] : null);
}
catch (Exception e)
{
    Console.Error.WriteLine($"[Deception] crashed: {e}");
    return 1;
}
return 0;