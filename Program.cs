using RecompOne.Runtime.Memory;
using Recompiled;

// Entry point for the Tecmo's Deception (SLUS-00340) port.
// Before the runtime reads settings.json or the memory cards.
Deception.LegacyData.Migrate();
Deception.GraphicsOptions.Install();
var memory = new PSMemory();
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