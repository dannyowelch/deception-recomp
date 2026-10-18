using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using BiosKernel = RecompOne.Runtime.Bios.Bios;

namespace RecompOne.Runtime.Dispatch;

public static class Dispatcher
{
    private static readonly OverlayLoadedEvent _overlayEvent = new();
    private static readonly Dictionary<string, IOverlay> _registry = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, string> _lbaToName = [];
    private static readonly List<string> _active = [];
    private static readonly Dictionary<uint, Action<CpuContext, IMemory>> _funcMap = [];
    // 0008. Functions of a still-resident overlay hidden by a newer one.
    // Victim name, then address, then every overlay that still covers it.
    // Rebuild skips them, and drops a cover once that overlay is gone.
    private static readonly Dictionary<string, Dictionary<uint, HashSet<string>>> _partial = new(StringComparer.OrdinalIgnoreCase);
    private static IOverlay? _pending;

    public static void Register(string name, IOverlay overlay)
    {
        _registry[name] = overlay;
        if (overlay.LbaStart >= 0) _lbaToName[overlay.LbaStart] = name;
    }

    public static string[] ActiveNames
    {
        get
        {
            lock (_active)
            {
                return _active.ToArray();
            }
        }
    }

    public static IReadOnlyDictionary<string, IOverlay> Overlays => _registry;

    public static void LoadByLba(int lba)
    {
        if (!_lbaToName.TryGetValue(lba, out var name)) return;
        var overlay = _registry[name];
        if (overlay.Base == 0)
        {
            Load(name);
            return;
        }

        _pending = overlay;
    }

    public static bool HasPending => _pending != null;

    public static void NotifyWrite(uint phys)
    {
        var p = _pending;
        if (p == null) return;

        PromoteWrite(p, phys);
    }

    private static void PromoteWrite(IOverlay p, uint phys)
    {
        var start = p.Base & 0x1FFFFFFFu;
        if (phys < start || phys >= start + 0x800u) return;
        _pending = null;
        Load(p.Name);
    }

    public static void ClearPending()
    {
        _pending = null;
    }

    public static void Reset()
    {
        _pending = null;
        lock (_active)
        {
            _active.Clear();
            _partial.Clear();
        }

        _funcMap.Clear();
    }

    public static void Load(string name)
    {
        if (!_registry.TryGetValue(name, out var overlay))
            throw new KeyNotFoundException($"overlay not registered: {name}");

        bool already;
        lock (_active)
        {
            already = _active.Remove(name);
        }

        // TEMP: which thread loaded which overlay, including a reload of one already resident.
        Console.WriteLine(
            $"[TEMP] overlay load {name} thread={Environment.CurrentManagedThreadId} reload={already}");

        if (!already) HandleRegionOverwrites(name, overlay);

        lock (_active)
        {
            _active.Add(name);
        }

        if (already)
        {
            // A reload writes its own entries back. Addresses a newer overlay
            // still holds stay out, or this would undo a partial eviction.
            lock (_active)
            {
                _partial.TryGetValue(name, out var hidden);
                foreach (var (addr, fn) in overlay.Functions)
                {
                    if (hidden != null && hidden.ContainsKey(addr)) continue;
                    _funcMap[addr] = fn;
                }
            }

            return;
        }

        // After the add, so Rebuild can see this overlay as resident and keep
        // the functions HandleRegionOverwrites just hid.
        Rebuild();
        Runtime.OverlayLog.Record(name, OverlayEventKind.Loaded);
        // TEMP: the guest range this overlay matched. Image [base, base+size) when
        // the overlay has one; otherwise the span of the function entries it bound.
        var lo = overlay.Base;
        var hi = overlay.Base + overlay.Size;
        if (overlay.Size == 0 && overlay.Functions.Count > 0)
        {
            lo = uint.MaxValue;
            hi = 0;
            foreach (var addr in overlay.Functions.Keys)
            {
                if (addr < lo) lo = addr;
                if (addr >= hi) hi = addr + 4;
            }
        }

        Console.WriteLine($"[Dispatcher] loaded overlay: {name} 0x{lo:X8}-0x{hi:X8}");

        if (Event.HasAnyListeners<OverlayLoadedEvent>())
        {
            var e = _overlayEvent;
            e.Context = Runtime.Cpu!;
            e.Memory = Runtime.Mem!;
            e.Name = name;
            Event.Dispatch(e);
        }
    }

    private static void HandleRegionOverwrites(string name, IOverlay overlay)
    {
        var newStart = overlay.Base & 0x1FFFFFFFu;
        var newEnd = newStart + overlay.Size;
        var hasRegion = overlay.Base != 0 && overlay.Size != 0;

        List<string>? overwritten = null;
        List<(string Name, int Dropped)>? partial = null;
        List<(string Name, int Funcs)>? vramCollisions = null;

        lock (_active)
        {
            foreach (var activeName in _active)
            {
                var other = _registry[activeName];
                var otherHasRegion = other.Base != 0 && other.Size != 0;

                if (hasRegion && otherHasRegion)
                {
                    var s = other.Base & 0x1FFFFFFFu;
                    var e = s + other.Size;

                    if (s < newEnd && e > newStart)
                    {
                        // 0008. Same base is an executable swap. OPEN/GAME/END
                        // share a base and the later file is often smaller, so
                        // the tail past its end has to go with the rest. A new
                        // image that covers the old one, or an old overlay with
                        // no function outside [newStart, newEnd), is the same
                        // whole-overlay drop. Anything else keeps the old
                        // overlay and hides only the functions in that range.
                        // Rebuild leaves them out while this overlay is resident.
                        var covered = s >= newStart && e <= newEnd;
                        if (s == newStart || covered || !FunctionOutside(other, newStart, newEnd))
                        {
                            overwritten ??= [];
                            overwritten.Add(activeName);
                            continue;
                        }

                        var dropped = HideOverlap(activeName, other, name, newStart, newEnd);
                        if (dropped > 0)
                        {
                            partial ??= [];
                            partial.Add((activeName, dropped));
                        }

                        continue;
                    }
                }

                var shared = CountSharedFunctions(overlay, other);
                if (shared > 0)
                {
                    vramCollisions ??= [];
                    vramCollisions.Add((activeName, shared));
                }
            }

            if (overwritten != null)
                foreach (var d in overwritten)
                {
                    _active.Remove(d);
                    _partial.Remove(d);
                }
        }

        if (overwritten != null)
        {
            foreach (var d in overwritten)
            {
                Runtime.OverlayLog.Record(d, OverlayEventKind.Overwritten, overlay.Name);
                Console.WriteLine($"[Dispatcher] overlay {d} overwritten by {overlay.Name}");
            }
        }

        if (partial != null)
            foreach (var (victim, n) in partial)
                Console.WriteLine(
                    $"[Dispatcher] overlay {victim} partially overwritten by {overlay.Name}: {n} functions dropped");

        if (vramCollisions != null)
            foreach (var (otherName, n) in vramCollisions)
            {
                Runtime.OverlayLog.Record(overlay.Name, OverlayEventKind.VramCollision, $"{otherName} ({n} funcs)");
                Console.WriteLine(
                    $"[Dispatcher] overlay {overlay.Name} vvram colision with {otherName}: {n} functions");
            }
    }

    // phys of addr is inside [start, end). Bases are masked the same way.
    private static bool FunctionOutside(IOverlay other, uint start, uint end)
    {
        foreach (var addr in other.Functions.Keys)
        {
            var phys = addr & 0x1FFFFFFFu;
            if (phys < start || phys >= end) return true;
        }

        return false;
    }

    private static int HideOverlap(string victim, IOverlay other, string by, uint start, uint end)
    {
        if (!_partial.TryGetValue(victim, out var hidden))
            _partial[victim] = hidden = new Dictionary<uint, HashSet<string>>();

        var n = 0;
        foreach (var addr in other.Functions.Keys)
        {
            var phys = addr & 0x1FFFFFFFu;
            if (phys < start || phys >= end) continue;
            if (!hidden.TryGetValue(addr, out var covers))
                hidden[addr] = covers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            covers.Add(by);
            n++;
        }

        if (hidden.Count == 0) _partial.Remove(victim);
        return n;
    }

    private static bool StillActive(string name)
    {
        foreach (var n in _active)
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    // Drop hide records whose victim or suppressor is no longer resident,
    // so a partial eviction lasts only while the newer overlay is loaded.
    private static void PrunePartial()
    {
        List<string>? dropVictims = null;
        foreach (var (victim, hidden) in _partial)
        {
            if (!StillActive(victim))
            {
                dropVictims ??= [];
                dropVictims.Add(victim);
                continue;
            }

            List<uint>? dropAddrs = null;
            foreach (var (addr, covers) in hidden)
            {
                covers.RemoveWhere(by => !StillActive(by));
                if (covers.Count == 0)
                {
                    dropAddrs ??= [];
                    dropAddrs.Add(addr);
                }
            }

            if (dropAddrs != null)
                foreach (var addr in dropAddrs)
                    hidden.Remove(addr);
            if (hidden.Count == 0)
            {
                dropVictims ??= [];
                dropVictims.Add(victim);
            }
        }

        if (dropVictims != null)
            foreach (var v in dropVictims)
                _partial.Remove(v);
    }

    private static int CountSharedFunctions(IOverlay a, IOverlay b)
    {
        var smaller = a.Functions.Count <= b.Functions.Count ? a : b;
        var larger = ReferenceEquals(smaller, a) ? b : a;

        var n = 0;
        foreach (var addr in smaller.Functions.Keys)
            if (larger.Functions.ContainsKey(addr))
                n++;
        return n;
    }

    public static void TryLoad(string name)
    {
        if (_registry.ContainsKey(name))
            Load(name);
    }

    public static void Unload(string name)
    {
        bool removed;
        lock (_active)
        {
            removed = _active.Remove(name);
        }

        if (!removed) return;
        Rebuild();
        Runtime.OverlayLog.Record(name, OverlayEventKind.Unloaded);
    }

    /// <summary>Is addr the entry point of a function in a resident overlay?</summary>
    public static bool HasFunction(uint addr) => _funcMap.ContainsKey(addr);

    public static bool CanCall(uint addr)
    {
        return addr switch
        {
            0xA0u or 0xB0u or 0xC0u => true,
            _ => (addr & 0xFF000000u) == 0xBFC00000u || _funcMap.ContainsKey(addr) ||
                 _funcMap.ContainsKey(Cached(addr))
        };
    }

    private static uint Cached(uint addr)
    {
        var phys = addr & 0x1FFFFFFFu;
        return phys < 0x00800000u ? 0x80000000u | phys : addr;
    }

    public static bool Tolerant;

    private static readonly HashSet<uint> _reported = [];

    // TEMP: one line per distinct kokumei-to-overlay situation. Remove with TempKokumeiCall.
    private static readonly HashSet<string> _tempKokumeiSeen = new();

    private static bool IsReturnSite(IMemory m, uint addr)
    {
        if (addr < 0x80000008u || (addr & 3u) != 0) return false;

        var w = m.ReadU32(addr - 8);
        var op = w >> 26;
        if (op == 3) return true;
        if (op == 0 && (w & 0x3Fu) == 9) return true;
        return op == 1 && ((w >> 16) & 0x1Fu) is 0x10 or 0x11;
    }

    //0107. The last indirect calls, for a crash or hang report (CrashReport): a
    //ring the game thread writes one word into per call, read only after the fact.
    private const int RecentCalls = 64;
    private static readonly uint[] _recent = new uint[RecentCalls];
    private static int _recentAt;

    /// <summary>The last indirect call targets, oldest first.</summary>
    public static uint[] Recent()
    {
        var at = _recentAt;
        var n = Math.Min(at, RecentCalls);
        var list = new uint[n];
        for (var i = 0; i < n; i++) list[i] = _recent[(at - n + i) & (RecentCalls - 1)];
        return list;
    }

    /// <summary>
    /// TEMP. A kokumei constant call. The call that follows is unchanged.
    /// Prints the class the call was compiled for ("dispatch" when the recompiler
    /// emitted Dispatcher.Call), the overlay a dispatcher lookup would bind this
    /// address to, and the overlays actually resident. One line per distinct
    /// site, target, binding, resident set and thread.
    /// </summary>
    public static void TempKokumeiCall(CpuContext c, string compiledFor, uint addr)
    {
        string resident;
        string slot;
        string mapped;
        lock (_active)
        {
            resident = _active.Count == 0 ? "(none)" : string.Join(",", _active);
            var slotNames = new List<string>();
            mapped = "(none)";
            foreach (var n in _active)
            {
                var overlay = _registry[n];
                if ((overlay.Base & 0x1FFFFFFFu) == 0x000B3578u)
                    slotNames.Add(n);
                if (_partial.TryGetValue(n, out var hidden) && hidden.ContainsKey(addr))
                    continue;
                if (overlay.Functions.ContainsKey(addr))
                    mapped = n;
            }

            slot = slotNames.Count == 0 ? "(none)" : string.Join(",", slotNames);
        }

        var thread = Environment.CurrentManagedThreadId;
        var site = c.RA;
        var id = string.Concat(
            compiledFor, "|", addr.ToString("X8"), "|", mapped, "|", slot, "|",
            resident, "|", site.ToString("X8"), "|", thread.ToString());
        lock (_tempKokumeiSeen)
            if (!_tempKokumeiSeen.Add(id)) return;

        Console.WriteLine(
            $"[TEMP] kokumei call 0x{addr:X8} from=0x{site:X8} compiled-for={compiledFor} mapped={mapped} slot={slot} resident={resident} thread={thread}");
    }

    public static void Call(CpuContext c, IMemory m, uint addr)
    {
        _recent[_recentAt++ & (RecentCalls - 1)] = addr;
        Diagnostics.Watchdog.Probe();
        if (BiosKernel.TryDispatch(c, m, addr)) return;

        if (_funcMap.TryGetValue(addr, out var fn))
        {
            fn(c, m);
            return;
        }

        var cached = Cached(addr);
        if (cached != addr && _funcMap.TryGetValue(cached, out fn))
        {
            fn(c, m);
            return;
        }
        
        if (IsReturnSite(m, addr)) return;

        if (!Tolerant) throw new InvalidOperationException($"unmapped call: 0x{addr:X8}");

        lock (_reported)
            if (_reported.Add(addr))
                Console.WriteLine($"[Dispatcher] skipped an unmapped call to 0x{addr:X8}");

        c.V0 = 0u;
    }

    private static void Rebuild()
    {
        _funcMap.Clear();
        lock (_active)
        {
            PrunePartial();
            foreach (var name in _active)
            {
                _partial.TryGetValue(name, out var hidden);
                foreach (var (addr, fn) in _registry[name].Functions)
                {
                    if (hidden != null && hidden.ContainsKey(addr)) continue;
                    _funcMap[addr] = fn;
                }
            }
        }
    }
}