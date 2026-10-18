using System.Diagnostics;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Diagnostics;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibEtc
{
    internal static double LastWaitMs = double.NegativeInfinity;
    
    private static int _vcount;
    private static readonly VSyncEvent _vsyncEvent = new();

    /// <summary>
    /// Which 60 Hz clock a waiting VSync sleeps on. Both clocks honor the same
    /// quota: <c>VSync(0)</c> and <c>VSync(1)</c> return once one vblank has passed
    /// since the previous waiting return, and <c>VSync(n)</c> for <c>n &gt; 1</c>
    /// waits for <c>n</c> vblanks (<c>VSync(2)</c> is 30 Hz). <c>VSync(n &lt; 0)</c>
    /// returns the count and does not wait or present. The default clock is the
    /// grid in this class. <c>KF2_VSYNC=block</c> uses <c>Interrupts</c> instead.
    /// A waiting call presents once, and that present does not also take
    /// <c>FrameClock</c>'s host ceiling.
    /// </summary>
    public static bool BlockingVSync;

    //0042. Presents counted where they happen, not where a hook says they do. A
    //port that paces from hooks on this function cannot use those hooks to notice
    //that they have stopped running, so this is counted in the body itself: the
    //calls that reach PresentFrame, and on request the managed stack of one of
    //them, which shows whether the call still came through the hook trampoline.
    public static long VSyncCalls;
    public static volatile bool CaptureNextStack;
    public static string? CapturedStack;

    //The vblank is time, not a call. On hardware the interrupt fires every 16.7 ms
    //whether or not the game is ready for it: a loop blocked on a CD read misses
    //pictures, not vblanks. This port advanced _vcount once per VSync call instead,
    //so every game-side clock hung on the vblank -- the music sequencer among them --
    //ran at the rendered frame rate: half speed on the CD-bound title screen (one
    //VSync call per ~15 fps picture), half again at KF2_FPS=15 (two calls per 66 ms
    //frame). Advance on a wall-clock grid and deliver the vblanks missed since the
    //last call as a burst, which is what the hardware's interrupt would have done
    //across the same gap.
    private static readonly Stopwatch VBlankClock = Stopwatch.StartNew();
    private static double _nextVBlankMs;
    private static bool _timelineStarted;

    private const double VBlankMs = 1000.0 / 60.0;

    //A host stall longer than this (window drag, breakpoint) resyncs the grid
    //instead of fast-forwarding the game through the whole gap.
    private const int MaxCatchUpVBlanks = 120;

    private static double HblankHz => LibGpu.Pal ? 15625.0 : 15734.0; //correct?

    private static int _lastVSyncCount;
    private static double _lastVSyncMs;

    public static void VSync(CpuContext c, IMemory m)
    {
        var mode = (int)c.A0;
        TempVSync(mode);
        if (Log.VSyncOn) Log.Sdk($"VSync({mode})");

        if (mode < 0)
        {
            c.V0 = BlockingVSync ? (uint)Interrupts.VBlankCount : (uint)_vcount;
            return;
        }

        VSyncCalls++;
        if (CaptureNextStack)
        {
            CaptureNextStack = false;
            CapturedStack = Environment.StackTrace;
        }

        LastWaitMs = Interrupts.ClockMs;
        //Upstream's frame interpolator feeds off this whichever timeline runs; it
        //is inert unless Interp is enabled.
        Interp.VideoRate.Push(mode == 0 ? 1 : mode);

        //0045. Only the presenting call is a section; the queries above return at
        //once. Not a try/finally: PresentFrame's hard reset is thrown before it
        //opens a section, and anything left open is closed by the next outer End.
        var profile = Profiler.Begin(Profiler.VSync);
        try
        {
            Present(c, m, mode);
        }
        finally
        {
            Profiler.End(profile);
        }
    }

    private static void Present(CpuContext c, IMemory m, int mode)
    {
        // VSync(0) is one vblank, the same quota as VSync(1). The anchor is the
        // previous waiting return, so a late caller does not wait out a fresh
        // period on top of vblanks that already passed.
        var count = mode == 0 ? 1 : mode;

        if (BlockingVSync)
        {
            WaitVBlanks(c, m, count);
            var elapsed = Elapsed();
            _lastVSyncCount = Interrupts.VBlankCount;
            _lastVSyncMs = Interrupts.ClockMs;
            _vcount++;

            if (Event.HasAnyListeners<VSyncEvent>())
            {
                var e = _vsyncEvent;
                e.Context = c;
                e.Memory = m;
                e.Frame = _vcount;
                Event.Dispatch(e);
            }

            PresentOnce();
            c.V0 = elapsed;
            return;
        }

        var vblank = Profiler.Begin(Profiler.VBlank);
        WaitVBlankQuota(c, m, count);
        Profiler.End(vblank);
        PresentOnce();
        c.V0 = 0;
    }

    // One present per VSync call. The vblank wait above is the pace, so the host
    // ceiling must not add another frame afterwards. Cleared if PresentFrame
    // throws before Throttle runs.
    private static void PresentOnce()
    {
        LastWaitMs = Interrupts.ClockMs;
        FrameClock.SkipNextThrottle = true;
        try
        {
            Runtime.PresentFrame();
        }
        finally
        {
            FrameClock.SkipNextThrottle = false;
        }
    }

    // _vcount at the previous VSync(mode >= 0) return. The first call has no
    // previous return: the anchor is taken after the opening catch-up, and that
    // call waits for a full quota.
    private static int _syncAnchor;
    private static bool _syncAnchored;

    private static void WaitVBlankQuota(CpuContext c, IMemory m, int count)
    {
        AdvanceVBlanks(c, m);
        if (!_syncAnchored)
        {
            _syncAnchor = _vcount;
            _syncAnchored = true;
        }

        var target = (long)_syncAnchor + count;
        while (_vcount < target)
        {
            // One grid step, then the vblank IRQ and the BIOS events on it.
            // Sleeping the whole quota at once would hold those off until the end.
            // LastWaitMs stays fresh so a flip during a long quota (VSync(60))
            // does not take a second present.
            LastWaitMs = Interrupts.ClockMs;
            SleepUntilNextVBlank();
            AdvanceVBlanks(c, m);
            Interrupts.PollNow(c, m);
        }

        _syncAnchor = _vcount;
    }

    private static void SleepUntilNextVBlank()
    {
        if (!_timelineStarted) return;

        var remaining = _nextVBlankMs - VBlankClock.Elapsed.TotalMilliseconds;
        if (remaining <= 0) return;

        if (remaining > SleepMarginMs)
        {
            var ms = (int)(remaining - SleepMarginMs);
            if (ms > 0) Thread.Sleep(ms);
        }
        else
        {
            Thread.SpinWait(64);
        }
    }

    //0091. A game that waits for its vblank handler without calling VSync gets its
    //vblanks from the interrupt poll instead. Off unless the port sets it.
    public static bool VBlankFromPoll;
    private static bool _advancing;

    internal static void PollVBlanks(CpuContext c, IMemory m)
    {
        if (BlockingVSync || _advancing) return;
        AdvanceVBlanks(c, m);
    }

    private static void AdvanceVBlanks(CpuContext c, IMemory m)
    {
        _advancing = true;
        try
        {
            AdvanceVBlanksOnGrid(c, m);
        }
        finally
        {
            _advancing = false;
        }
    }

    private static void AdvanceVBlanksOnGrid(CpuContext c, IMemory m)
    {
        var now = VBlankClock.Elapsed.TotalMilliseconds;
        if (!_timelineStarted)
        {
            _timelineStarted = true;
            _nextVBlankMs = now;
        }
        else if (now - _nextVBlankMs > MaxCatchUpVBlanks * VBlankMs)
        {
            // The host was stopped, so discard its stale vblanks; retain only
            // the current boundary below rather than fast-forwarding the game.
            _nextVBlankMs = now;
        }

        var n = 0;
        while (_nextVBlankMs <= now && n < MaxCatchUpVBlanks)
        {
            _nextVBlankMs += VBlankMs;
            n++;
            TickVBlank(c, m);
        }

        //The loop cap is the same size as the resync threshold, so it only bites at
        //the exact boundary; if it ever does, snap the grid to now rather than carry
        //the leftover gap into the next call and lag a frame behind for good.
        if (n == MaxCatchUpVBlanks && _nextVBlankMs < now) _nextVBlankMs = now;
    }

    private static void TickVBlank(CpuContext c, IMemory m)
    {
        TempVBlank();
        _vcount++;

        //0021: RCntCNT3/EvSpINT, the vblank root counter's event, is not delivered
        //here. IRQ 0 below delivers it (Interrupts.ServiceIrq -> DeliverIrqEvents),
        //as on upstream's blocking timeline; delivering it here as well ran every
        //handler on it twice a vblank.

        if (Event.HasAnyListeners<VSyncEvent>())
        {
            var e = _vsyncEvent;
            e.Context = c;
            e.Memory = m;
            e.Frame = _vcount;
            Event.Dispatch(e);
        }

        //The PSY-Q vblank callback (IRQ 0) belongs to the vblank too, not to the
        //VSync call -- the game's own frame counter hangs off it.
        Runtime.DispatchIrq(0);
    }

    private static uint Elapsed()
    {
        return (uint)((Interrupts.ClockMs - _lastVSyncMs) * HblankHz / 1000.0) & 0xFFFF;
    }

    private const double SleepMarginMs = 2.0;

    private static void WaitVBlanks(CpuContext c, IMemory m, int count)
    {
        var profile = Profiler.Begin(Profiler.VBlankWait);
        WaitVBlanksCore(c, m, count);
        Profiler.End(profile);
    }

    private static void WaitVBlanksCore(CpuContext c, IMemory m, int count)
    {
        var target = _lastVSyncCount + count;

        while (Interrupts.VBlankCount < target)
        {
            LastWaitMs = Interrupts.ClockMs;
            if (Interrupts.Turbo)
            {
                Interrupts.ForceVBlank(c, m);
                continue;
            }

            var remaining = Interrupts.MsToNextVBlank;
            if (remaining > SleepMarginMs)
            {
                var ms = (int)(remaining - SleepMarginMs);
                if (ms > 0) Thread.Sleep(ms);
            }
            else
            {
                Thread.SpinWait(64);
            }

            Interrupts.PollNow(c, m);
        }
    }

    // TEMP. One line a second. vblanks is each delivery on this grid (a catch-up
    // burst counts as many). vsync is every VSync call, grouped by its argument.
    // loops is kokumei's main loop at 0x800174EC, emitted by the recompiler.
    // presents is Runtime.PresentFrame. Not a permanent API, and it does not
    // wait, sleep, or move the grid.
    private static long _tempVblanks;
    private static long _tempLoops;
    private static long _tempPresents;
    private static readonly Dictionary<int, long> _tempVsync = new();
    private static double _tempLogAt = -1;

    private static void TempVSync(int mode)
    {
        _tempVsync.TryGetValue(mode, out var n);
        _tempVsync[mode] = n + 1;
        TempPaceLog();
    }

    private static void TempVBlank()
    {
        _tempVblanks++;
        TempPaceLog();
    }

    public static void NoteGameLoop()
    {
        _tempLoops++;
        TempPaceLog();
    }

    internal static void NotePresent()
    {
        _tempPresents++;
        TempPaceLog();
    }

    private static void TempPaceLog()
    {
        var now = VBlankClock.Elapsed.TotalMilliseconds;
        if (_tempLogAt < 0)
        {
            _tempLogAt = now;
            return;
        }

        var elapsed = now - _tempLogAt;
        if (elapsed < 1000.0) return;

        var keys = new List<int>(_tempVsync.Keys);
        keys.Sort();
        var vs = "none";
        if (keys.Count > 0)
        {
            var parts = new string[keys.Count];
            for (var i = 0; i < keys.Count; i++)
                parts[i] = $"{keys[i]}:{_tempVsync[keys[i]]}";
            vs = string.Join(",", parts);
        }

        Console.WriteLine(
            $"[TEMP] pace {elapsed:0}ms vblanks={_tempVblanks} vsync={vs} loops={_tempLoops} presents={_tempPresents}");
        _tempVblanks = 0;
        _tempLoops = 0;
        _tempPresents = 0;
        _tempVsync.Clear();
        _tempLogAt = now;
    }
}
