using RecompOne.Runtime.Hle;

namespace RecompOne.Runtime;

public sealed partial class Gpu
{
    bool HleOn => !Detached && GpuHle.Active && GpuHle.Backend is { Ready: true };

    int CurTPage() => ((_texPageX / 64) & 0xf) | (((_texPageY / 256) & 1) << 4)
                    | ((_blendMode & 3) << 5) | ((_texDepth & 3) << 7);

    HleDrawEnv CurEnv() => new()
    {
        ClipX0 = _drawAreaLeft, ClipY0 = _drawAreaTop, ClipX1 = _drawAreaRight, ClipY1 = _drawAreaBottom,
        TwMaskX = _texWinMaskX, TwMaskY = _texWinMaskY, TwOffX = _texWinOffX, TwOffY = _texWinOffY,
        SetMask = _setMask, CheckMask = _checkMask, Dither = _dither,
    };

    // The backend has taken a float position all along, so the sub-pixel fraction
    // needs nothing of it: adding the fraction back here is the whole of the
    // hardware path. Fx and Fy are zero for a vertex that recovered nothing, and
    // for every vertex at all while the setting is off, so this is an exact no-op
    // in both cases rather than an almost-no-op.
    //
    // HasPersp and HasGteZ are independent: the first is "use Z as clip W" (the
    // perspective-correct path), the second is "this Z is a real view depth" for
    // the Z-buffer. Untextured walls have the second without the first.
    static HleVertex HV(in Vert v, bool persp, bool z) => new()
    {
        X = v.X + v.Fx, Y = v.Y + v.Fy,
        R = (byte)v.R, G = (byte)v.G, B = (byte)v.B, U = (short)v.U, V = (short)v.V,
        Z = v.W, HasGteZ = z, HasPersp = persp,
        Lx = v.Lx, Ly = v.Ly, Lz = v.Lz, Fog = v.Fog, Light = v.Light, LightGen = v.LightGen,
        TexRect = v.TexRect, HasTexRect = v.HasTexRect, Solid = v.Solid, Material = v.Material, NotRect = v.NotRect, Projected = v.Projected,
    };

    PrimFlags PrimOf(bool tex, bool semi, bool raw, int clut, bool gouraud = false) => new()
    {
        Textured = tex, SemiTrans = semi, RawTexture = raw, Gouraud = gouraud, TPage = (ushort)CurTPage(), Clut = (ushort)clut,
    };

    void HleTri(in Vert a, in Vert b, in Vert c, bool tex, bool gouraud, bool semi, bool raw, int clut)
    {
        int spanX = Math.Max(a.X, Math.Max(b.X, c.X)) - Math.Min(a.X, Math.Min(b.X, c.X));
        int spanY = Math.Max(a.Y, Math.Max(b.Y, c.Y)) - Math.Min(a.Y, Math.Min(b.Y, c.Y));
        // The GPU refuses a polygon wider than 1023 or taller than 511. A wall
        // against the camera is one of those, and dropping it drops the part that
        // is on screen: a black hole, or the surface behind it. Clip that polygon
        // to the picture and draw the piece that fits. A polygon the GPU accepts
        // is not touched.
        bool oversized = spanX > 1023 || spanY > 511;

        // All three or none, as in the software path: the backend turns a vertex's
        // depth into gl_Position.w, and one vertex left at 1 would tear the
        // triangle's texture in half. The Z-buffer is the same rule for a different
        // reason — a corner at ndc.z = 0 among two real depths would punch a hole.
        // DepthWanted rather than ZBuffer: ambient occlusion reads the same
        // attachment and so needs the same writes, and differs only in that
        // nothing is ever rejected by them (GlCore.Flush leaves the func at
        // GL_ALWAYS unless the Z-buffer is on as well).
        bool z = GteDepth.DepthWanted && GteDepth.OtSlot != 0 && a.HasZ && b.HasZ && c.HasZ;

        // **A depth-tested triangle gets a real clip W whether or not its texture
        // is being corrected**, and that is a fix rather than tidiness. `vDepth` is
        // an ordinary varying, so OpenGL interpolates it in 1/w -- which is exact
        // when w is the view depth and *screen-linear* when w is 1. An untextured
        // wall never asked for texture correction, so it used to arrive with w = 1
        // and its interior depths came out linear in screen space, which is the one
        // thing a view depth is not: it is 1/z that is affine across a pixel row.
        // The software rasterizer has always interpolated the reciprocals and taken
        // one back (see `useZ` in DrawPolygon); this is the same arithmetic, done by
        // the rasterizer instead of by hand, and it makes the two agree.
        //
        // The visible cost is that a textured triangle drawn with the depth buffer
        // on and perspective correction off is now corrected anyway. That pair is a
        // comparison rather than a picture anyone ships, and a depth buffer fed
        // wrong depths is not a comparison of anything.
        bool persp = z || (tex && a.HasW && b.HasW && c.HasW);
        if (GteDepth.DepthWanted) { if (z) GteDepth.ZTris++; else GteDepth.ZSkipped++; }

        var be = GpuHle.Backend!;
        be.SetDrawEnv(CurEnv());
        var flags = PrimOf(tex, semi, raw, clut, gouraud);
        var va = HV(a, persp, z);
        var vb = HV(b, persp, z);
        var vc = HV(c, persp, z);
        if (!oversized)
        {
            be.DrawTri(va, vb, vc, flags);
            return;
        }

        PictureClip(out float left, out float top, out float right, out float bottom);
        Span<HleVertex> poly = stackalloc HleVertex[12];
        Span<HleVertex> tmp = stackalloc HleVertex[12];
        int n = ClipPicture(va, vb, vc, left, top, right, bottom, poly, tmp);
        for (int i = 1; i < n - 1; i++)
        {
            if (!FitsGpu(poly[0], poly[i], poly[i + 1])) continue;
            be.DrawTri(poly[0], poly[i], poly[i + 1], flags);
        }
    }

    // The wide picture adds columns beside the draw area. A clip that already
    // covers the display is the one the scissor opens, so an oversized wall is
    // cut to that same rectangle. Anything else keeps the game's own clip.
    void PictureClip(out float left, out float top, out float right, out float bottom)
    {
        left = _drawAreaLeft;
        top = _drawAreaTop;
        right = _drawAreaRight;
        bottom = _drawAreaBottom;
        int dw = GpuHle.LastDisplayW;
        if (dw <= 0) return;
        int margin = GpuHle.WideMargin(dw);
        if (margin <= 0) return;
        for (int i = 0; i < GpuHle.RectCount; i++)
        {
            var r = GpuHle.GetRect(i);
            if (!r.Valid || r.W != dw) continue;
            if (_drawAreaLeft <= r.X && _drawAreaRight >= r.X + r.W - 1)
            {
                left = r.X - margin;
                right = r.X + r.W - 1 + margin;
                return;
            }
        }
    }

    static int ClipPicture(in HleVertex a, in HleVertex b, in HleVertex c,
        float left, float top, float right, float bottom, Span<HleVertex> poly, Span<HleVertex> tmp)
    {
        tmp[0] = a; tmp[1] = b; tmp[2] = c;
        int n = ClipAxis(tmp, 3, poly, 0, left, false);
        if (n < 3) return 0;
        n = ClipAxis(poly, n, tmp, 0, right, true);
        if (n < 3) return 0;
        n = ClipAxis(tmp, n, poly, 1, top, false);
        if (n < 3) return 0;
        n = ClipAxis(poly, n, tmp, 1, bottom, true);
        if (n < 3) return 0;
        tmp.Slice(0, n).CopyTo(poly);
        return n;
    }

    // keepLess: the inside is the side at or below the edge. Otherwise at or above.
    static int ClipAxis(ReadOnlySpan<HleVertex> src, int n, Span<HleVertex> dst, int axis, float edge, bool keepLess)
    {
        int m = 0;
        for (int i = 0; i < n; i++)
        {
            var s = src[i];
            var e = src[(i + 1) % n];
            float sv = axis == 0 ? s.X : s.Y;
            float ev = axis == 0 ? e.X : e.Y;
            bool sin = keepLess ? sv <= edge : sv >= edge;
            bool ein = keepLess ? ev <= edge : ev >= edge;
            if (m >= dst.Length - 1) break;
            if (sin && ein)
            {
                dst[m++] = e;
            }
            else if (sin != ein)
            {
                float den = ev - sv;
                float t = den == 0f ? 0f : (edge - sv) / den;
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                dst[m++] = LerpVert(s, e, t);
                if (ein) dst[m++] = e;
            }
        }
        return m;
    }

    static HleVertex LerpVert(in HleVertex a, in HleVertex b, float t)
    {
        var d = a;
        d.X = a.X + (b.X - a.X) * t;
        d.Y = a.Y + (b.Y - a.Y) * t;
        bool persp = a.HasPersp && b.HasPersp && a.Z > 1e-3f && b.Z > 1e-3f;
        if (persp)
        {
            float iz = (1f / a.Z) + ((1f / b.Z) - (1f / a.Z)) * t;
            if (iz > 1e-12f && float.IsFinite(iz))
            {
                d.Z = 1f / iz;
                d.U = Attr(a.U, a.Z, b.U, b.Z, t, iz);
                d.V = Attr(a.V, a.Z, b.V, b.Z, t, iz);
                d.R = ByteAttr(a.R, a.Z, b.R, b.Z, t, iz);
                d.G = ByteAttr(a.G, a.Z, b.G, b.Z, t, iz);
                d.B = ByteAttr(a.B, a.Z, b.B, b.Z, t, iz);
                d.Lx = Attr(a.Lx, a.Z, b.Lx, b.Z, t, iz);
                d.Ly = Attr(a.Ly, a.Z, b.Ly, b.Z, t, iz);
                d.Lz = Attr(a.Lz, a.Z, b.Lz, b.Z, t, iz);
                d.Fog = Attr(a.Fog, a.Z, b.Fog, b.Z, t, iz);
                d.Depth = Attr(a.Depth, a.Z, b.Depth, b.Z, t, iz);
                d.HasGteZ = a.HasGteZ && b.HasGteZ;
                d.HasPersp = true;
                return d;
            }
        }

        d.Z = a.Z + (b.Z - a.Z) * t;
        d.U = a.U + (b.U - a.U) * t;
        d.V = a.V + (b.V - a.V) * t;
        d.R = MixByte(a.R, b.R, t);
        d.G = MixByte(a.G, b.G, t);
        d.B = MixByte(a.B, b.B, t);
        d.Lx = a.Lx + (b.Lx - a.Lx) * t;
        d.Ly = a.Ly + (b.Ly - a.Ly) * t;
        d.Lz = a.Lz + (b.Lz - a.Lz) * t;
        d.Fog = a.Fog + (b.Fog - a.Fog) * t;
        d.Depth = a.Depth + (b.Depth - a.Depth) * t;
        d.HasGteZ = a.HasGteZ && b.HasGteZ && d.Z > 0f;
        d.HasPersp = a.HasPersp && b.HasPersp && d.Z > 0f;
        return d;
    }

    static float Attr(float a, float za, float b, float zb, float t, float iz)
    {
        float wa = 1f / za, wb = 1f / zb;
        return (a * wa + (b * wb - a * wa) * t) / iz;
    }

    static byte ByteAttr(byte a, float za, byte b, float zb, float t, float iz) =>
        (byte)Math.Clamp((int)MathF.Round(Attr(a, za, b, zb, t, iz)), 0, 255);

    static byte MixByte(byte a, byte b, float t) =>
        (byte)Math.Clamp((int)MathF.Round(a + (b - a) * t), 0, 255);

    static bool FitsGpu(in HleVertex a, in HleVertex b, in HleVertex c)
    {
        float minX = MathF.Min(a.X, MathF.Min(b.X, c.X));
        float maxX = MathF.Max(a.X, MathF.Max(b.X, c.X));
        float minY = MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
        float maxY = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
        return maxX - minX <= 1023f && maxY - minY <= 511f;
    }

    void HleRect(int x, int y, int w, int h, int u, int v, int clut, int r, int g, int b, bool tex, bool semi, bool raw)
    {
        var be = GpuHle.Backend!;
        be.SetDrawEnv(CurEnv());
        be.DrawRect(new HleRect { X = x, Y = y, W = w, H = h, U = (short)u, V = (short)v, R = (byte)r, G = (byte)g, B = (byte)b },
            PrimOf(tex, semi, raw, clut));
    }

    void HleLine(int x0, int y0, int r0, int g0, int b0, int x1, int y1, int r1, int g1, int b1, bool semi, bool gouraud)
    {
        if (Math.Abs(x1 - x0) > 1023 || Math.Abs(y1 - y0) > 511) return;

        var be = GpuHle.Backend!;
        be.SetDrawEnv(CurEnv());
        be.DrawLine(
            new HleVertex { X = x0, Y = y0, R = (byte)r0, G = (byte)g0, B = (byte)b0 },
            new HleVertex { X = x1, Y = y1, R = (byte)r1, G = (byte)g1, B = (byte)b1 },
            PrimOf(false, semi, false, 0, gouraud));
    }

    void HleFill(int x, int y, int w, int h, ushort color) => GpuHle.Backend!.FillRect(x, y, w, h, color);
    void HleCopy(int sx, int sy, int dx, int dy, int w, int h) => GpuHle.Backend!.CopyVram(sx, sy, dx, dy, w, h);

    ushort[] _readBuf = Array.Empty<ushort>();

    void HleReadback(int x, int y, int w, int h)
    {
        int n = w * h;
        if (_readBuf.Length < n) _readBuf = new ushort[n];
        var buf = _readBuf;

        if (Host.GpuJobs.Claimed && !Host.GpuJobs.IsOwner)
        {
            Host.GpuJobs.Run(() => GpuHle.Backend!.ReadVram(x, y, w, h, buf));
        }
        else
        {
            GpuHle.Backend!.ReadVram(x, y, w, h, buf);
        }

        for (int row = 0; row < h; row++)
        {
            int dst = ((y + row) & (VramHeight - 1)) * VramWidth;
            for (int col = 0; col < w; col++)
                Vram[dst + ((x + col) & (VramWidth - 1))] = _readBuf[row * w + col];
        }
        Assets.Textures.VramTracker.MarkCpuWrite(x, y, w, h);
    }

    //img load
    ushort[] _hleLoad = Array.Empty<ushort>();
    bool _hleLoadActive;
    int _hleLoadPos;

    void HleLoadBegin()
    {
        _hleLoadActive = HleOn;
        if (!_hleLoadActive) return;
        int n = _loadW * _loadH;
        if (_hleLoad.Length < n) _hleLoad = new ushort[n];
        _hleLoadPos = 0;
    }

    void HleLoadPut(ushort value)
    {
        if (_hleLoadActive && _hleLoadPos < _hleLoad.Length) _hleLoad[_hleLoadPos++] = value;
    }

    void HleLoadFlush()
    {
        if (!_hleLoadActive) return;
        _hleLoadActive = false;
        var be = GpuHle.Backend!;
        var px = _hleLoad.AsSpan(0, _loadW * _loadH);
        if (_loadX + _loadW <= VramWidth && _loadY + _loadH <= VramHeight)
        {
            be.WriteVram(_loadX, _loadY, _loadW, _loadH, px);
            return;
        }

        //0092. A load past VRAM's right or bottom edge wraps, as StoreImageHalfword
        //writes it; a backend's rectangle cannot, so it goes as the pieces either side.
        int w0 = Math.Min(_loadW, VramWidth - _loadX), h0 = Math.Min(_loadH, VramHeight - _loadY);
        Piece(be, px, 0, 0, w0, h0);
        Piece(be, px, w0, 0, _loadW - w0, h0);
        Piece(be, px, 0, h0, w0, _loadH - h0);
        Piece(be, px, w0, h0, _loadW - w0, _loadH - h0);
    }

    ushort[] _hlePiece = Array.Empty<ushort>();

    void Piece(IGpuBackend be, ReadOnlySpan<ushort> px, int col, int row, int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        if (_hlePiece.Length < w * h) _hlePiece = new ushort[w * h];
        for (int r = 0; r < h; r++)
            px.Slice((row + r) * _loadW + col, w).CopyTo(_hlePiece.AsSpan(r * w, w));
        int dx = (_loadX + col) & (VramWidth - 1), dy = (_loadY + row) & (VramHeight - 1);
        be.WriteVram(dx, dy, w, h, _hlePiece.AsSpan(0, w * h));
    }
}
