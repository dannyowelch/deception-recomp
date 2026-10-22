using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Deception;

/// <summary>
/// The frame's two primitive buffers. Each is 48000 bytes, at
/// <c>0x80087F34 + index * 48000</c>, and the index at <c>0x80086944</c> is only
/// ever 0 or 1. They sit back to back and end on the word at <c>0x8009F638</c>.
/// <c>func_80031518</c> publishes the current one at <c>0x8007000C</c>, and the
/// allocators bump that pointer with no end test. Packets past 48000 bytes walk
/// into the data that follows the pair.
///
/// A post on that publisher parks index 0 at <c>0x80200000</c> and index 1 at
/// <c>0x80400000</c>, 2 MB each, inside an 8 MB guest. The packet link keeps the
/// low 24 bits (the length byte replaces the <c>0x80</c> prefix). Those addresses
/// sit below the <c>0x800000</c> bit <c>DrawOTag</c> treats as the end of a list.
///
/// Twelve room loops still subtract the pointer from <c>0x80093AB4 + index * 48000</c>
/// and skip the model when fewer than 5000 or 6500 bytes remain. The relocated
/// pointer makes that difference negative, so <c>DeceptionLodRewrite</c> moves
/// the end to <c>0x805F4480</c>. Index 1 then ends at <c>0x80600000</c>. Index 0
/// reports about 4 MB free against a 2 MB slot.
/// </summary>
public static class PrimHeap
{
    public const uint RamBytes = MemoryMap.DevkitRamSize;

    const uint Publisher = 0x80031518;
    const uint Pointer = 0x8007000C;
    const uint StockBase = 0x80087F34;
    const uint StockBytes = 48000;
    const int Slots = 2;
    const uint HeapBase = 0x80200000;
    const uint SlotBytes = 0x200000;

    static readonly ModInfo Self = new()
    {
        Id = "deception.primheap",
        Name = "Deception primitive heap",
        Version = "1",
    };

    static bool _logged;
    static bool _odd;
    static bool _filled;

    /// <summary>True once the publisher stores into the heap above 2 MB.</summary>
    public static bool Ready { get; private set; }

    public static void Install()
    {
        HookAttach.OnOverlayLoad("prim heap", Attach,
            "Sharp walls stay on the blocky page until the primitive buffers move.");
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("kokumei", null, Publisher);
        if (target?.Name != "func_80031518") return false;
        if (!HookAttach.Installed(target))
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            HookManager.AddPre(Self, target, typeof(PrimHeap).GetMethod(nameof(OnBefore), flags)!);
            HookManager.AddPost(Self, target, typeof(PrimHeap).GetMethod(nameof(OnPublish), flags)!);
            HookManager.Commit();
        }

        if (!HookAttach.Installed(target)) return false;
        Ready = true;
        Console.WriteLine($"[deception] prim buffers {Slots} x {SlotBytes >> 20} MB at 0x{HeapBase:X8}");
        return true;
    }

    /// <summary>
    /// The pointer still holds the end of the buffer the previous publish
    /// started. One line, the first time that end sits in the relocated pair.
    /// </summary>
    public static void OnBefore(CpuContext c, IMemory m)
    {
        if (_filled) return;
        uint end = m.ReadU32(Pointer);
        if (end < HeapBase) return;
        uint span = (uint)Slots * SlotBytes;
        if (end >= HeapBase + span) return;
        uint slot = (end - HeapBase) / SlotBytes;
        uint used = end - (HeapBase + slot * SlotBytes);
        if (used == 0) return;
        _filled = true;
        Console.WriteLine($"[deception] prim buffer filled {used} bytes (the stock pair holds {StockBytes})");
    }

    public static void OnPublish(CpuContext c, IMemory m)
    {
        uint stock = m.ReadU32(Pointer);
        uint moved = Move(stock);
        if (moved == stock)
        {
            if (!_odd && stock >= StockBase && stock < StockBase + (uint)Slots * StockBytes)
            {
                _odd = true;
                Console.Error.WriteLine($"[deception] prim pointer 0x{stock:X8} was left in the 48000-byte pair");
            }
            return;
        }

        m.WriteU32(Pointer, moved);
        if (_logged) return;
        _logged = true;
        Console.WriteLine($"[deception] prim buffer 0x{stock:X8} -> 0x{moved:X8}");
    }

    static uint Move(uint addr)
    {
        if (addr < StockBase) return addr;
        uint off = addr - StockBase;
        uint index = off / StockBytes;
        if (index >= Slots || off % StockBytes != 0) return addr;
        return HeapBase + index * SlotBytes;
    }
}
