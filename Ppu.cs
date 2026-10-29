public class Ppu
{
    static readonly int[] Pal = {
        0x666666,0x002A88,0x1412A7,0x3B00A4,0x5C007E,0x6E0040,0x6C0600,0x561D00,0x333500,0x0B4800,0x005200,0x004F08,0x00404D,0,0,0,
        0xADADAD,0x155FD9,0x4240FF,0x7527FE,0xA01ACC,0xB71E7B,0xB53120,0x994E00,0x6B6D00,0x388700,0x0C9300,0x008F32,0x007C8D,0,0,0,
        0xFFFEFF,0x64B0FF,0x9290FF,0xC676FF,0xF36AFF,0xFE6ECC,0xFE8170,0xEA9E22,0xBCBE00,0x88D800,0x5CE430,0x45E082,0x48CDDE,0x4F4F4F,0,0,
        0xFFFEFF,0xC0DFFF,0xD3D2FF,0xE8C8FF,0xFBC2FF,0xFEC4EA,0xFECCC5,0xF7D8A5,0xE4E594,0xCFEF96,0xBDF4AB,0xB3F3CC,0xB5EBF2,0xB8B8B8,0,0 };

    public int[] Frame = new int[256 * 240];
    public byte[] Oam = new byte[256];
    public bool Nmi, FrameDone;
    public byte OamAddr;

    readonly Cartridge cart;
    readonly byte[] vram = new byte[2048], pal = new byte[32];
    byte ctrl, mask, status, readBuf;
    int v, t, fineX, w;
    int scanline, cycle;
    bool odd;

    // background pipeline
    int bgLo, bgHi, bgAtLo, bgAtHi;
    byte nId, nAttr, nLo, nHi;
    // sprites for next scanline
    int sCount; bool spr0Possible, spr0Rendering;
    readonly byte[] sY = new byte[8], sTile = new byte[8], sAttr = new byte[8], sX = new byte[8], sLo = new byte[8], sHi = new byte[8];

    public Ppu(Cartridge c) { cart = c; }

    int Nt(int a)
    {
        int tb = (a >> 10) & 3;
        tb = cart.Mirroring switch { Mirror.Horizontal => tb >> 1, Mirror.Vertical => tb & 1, Mirror.Single0 => 0, _ => 1 };
        return (tb << 10) | (a & 0x3FF);
    }

    byte Rd(int a)
    {
        a &= 0x3FFF;
        if (a < 0x2000) return cart.PpuRead((ushort)a);
        if (a < 0x3F00) return vram[Nt(a)];
        a &= 0x1F; if ((a & 0x13) == 0x10) a ^= 0x10;
        return pal[a];
    }

    void Wr(int a, byte d)
    {
        a &= 0x3FFF;
        if (a < 0x2000) cart.PpuWrite((ushort)a, d);
        else if (a < 0x3F00) vram[Nt(a)] = d;
        else { a &= 0x1F; if ((a & 0x13) == 0x10) a ^= 0x10; pal[a] = d; }
    }

    public byte RegRead(int r)
    {
        byte d = 0;
        switch (r)
        {
            case 2: d = (byte)((status & 0xE0) | (readBuf & 0x1F)); status &= 0x7F; w = 0; break;
            case 4: d = Oam[OamAddr]; break;
            case 7:
                d = readBuf; readBuf = Rd(v);
                if ((v & 0x3FFF) >= 0x3F00) d = readBuf;
                v += (ctrl & 4) != 0 ? 32 : 1; break;
        }
        return d;
    }

    public void RegWrite(int r, byte d)
    {
        switch (r)
        {
            case 0:
                bool wasOff = (ctrl & 0x80) == 0;
                ctrl = d; t = (t & ~0x0C00) | ((d & 3) << 10);
                if (wasOff && (d & 0x80) != 0 && (status & 0x80) != 0) Nmi = true;
                break;
            case 1: mask = d; break;
            case 3: OamAddr = d; break;
            case 4: Oam[OamAddr++] = d; break;
            case 5:
                if (w == 0) { fineX = d & 7; t = (t & ~0x1F) | (d >> 3); w = 1; }
                else { t = (t & ~0x73E0) | ((d & 7) << 12) | ((d & 0xF8) << 2); w = 0; }
                break;
            case 6:
                if (w == 0) { t = (t & 0x00FF) | ((d & 0x3F) << 8); w = 1; }
                else { t = (t & 0xFF00) | d; v = t; w = 0; }
                break;
            case 7: Wr(v, d); v += (ctrl & 4) != 0 ? 32 : 1; break;
        }
    }

    void IncX() { if ((v & 0x1F) == 31) { v &= ~0x1F; v ^= 0x400; } else v++; }
    void IncY()
    {
        if ((v & 0x7000) != 0x7000) { v += 0x1000; return; }
        v &= ~0x7000;
        int y = (v & 0x3E0) >> 5;
        if (y == 29) { y = 0; v ^= 0x800; } else if (y == 31) y = 0; else y++;
        v = (v & ~0x3E0) | (y << 5);
    }

    void LoadBg()
    {
        bgLo = (bgLo & 0xFF00) | nLo; bgHi = (bgHi & 0xFF00) | nHi;
        bgAtLo = (bgAtLo & 0xFF00) | ((nAttr & 1) != 0 ? 0xFF : 0);
        bgAtHi = (bgAtHi & 0xFF00) | ((nAttr & 2) != 0 ? 0xFF : 0);
    }

    void Shift()
    {
        if ((mask & 8) != 0) { bgLo <<= 1; bgHi <<= 1; bgAtLo <<= 1; bgAtHi <<= 1; }
        if ((mask & 0x10) != 0 && cycle >= 2 && cycle < 258)
            for (int i = 0; i < sCount; i++)
            {
                if (sX[i] > 0) sX[i]--;
                else { sLo[i] <<= 1; sHi[i] <<= 1; }
            }
    }

    void EvalSprites()
    {
        sCount = 0; spr0Possible = false;
        int size = (ctrl & 0x20) != 0 ? 16 : 8;
        for (int n = 0; n < 64; n++)
        {
            int diff = scanline - Oam[n * 4];
            if (diff < 0 || diff >= size) continue;
            if (sCount >= 8) { status |= 0x20; break; }
            if (n == 0) spr0Possible = true;
            sY[sCount] = Oam[n * 4]; sTile[sCount] = Oam[n * 4 + 1]; sAttr[sCount] = Oam[n * 4 + 2]; sX[sCount] = Oam[n * 4 + 3];
            sCount++;
        }
    }

    void FetchSprites()
    {
        for (int i = 0; i < sCount; i++)
        {
            int row = scanline - sY[i], addr;
            bool fv = (sAttr[i] & 0x80) != 0;
            if ((ctrl & 0x20) == 0)
            {
                if (fv) row = 7 - row;
                addr = ((ctrl & 8) << 9) | (sTile[i] << 4) | row;
            }
            else
            {
                if (fv) row = 15 - row;
                int tile = (sTile[i] & 0xFE) + (row >= 8 ? 1 : 0);
                addr = ((sTile[i] & 1) << 12) | (tile << 4) | (row & 7);
            }
            byte lo = Rd(addr), hi = Rd(addr + 8);
            if ((sAttr[i] & 0x40) != 0) { lo = Flip(lo); hi = Flip(hi); }
            sLo[i] = lo; sHi[i] = hi;
        }
    }

    static byte Flip(byte b)
    {
        b = (byte)((b & 0xF0) >> 4 | (b & 0x0F) << 4);
        b = (byte)((b & 0xCC) >> 2 | (b & 0x33) << 2);
        return (byte)((b & 0xAA) >> 1 | (b & 0x55) << 1);
    }

    public void Clock()
    {
        bool rend = (mask & 0x18) != 0;
        if (scanline >= -1 && scanline < 240)
        {
            if (scanline == 0 && cycle == 0 && odd && rend) cycle = 1;
            if (scanline == -1 && cycle == 1) status &= 0x1F;

            if ((cycle >= 2 && cycle < 258) || (cycle >= 321 && cycle < 338))
            {
                Shift();
                int bgBase = (ctrl & 0x10) << 8;
                switch ((cycle - 1) % 8)
                {
                    case 0: LoadBg(); nId = Rd(0x2000 | (v & 0x0FFF)); break;
                    case 2:
                        nAttr = Rd(0x23C0 | (v & 0x0C00) | ((v >> 4) & 0x38) | ((v >> 2) & 7));
                        if ((v & 0x40) != 0) nAttr >>= 4;
                        if ((v & 0x02) != 0) nAttr >>= 2;
                        nAttr &= 3; break;
                    case 4: nLo = Rd(bgBase + (nId << 4) + ((v >> 12) & 7)); break;
                    case 6: nHi = Rd(bgBase + (nId << 4) + ((v >> 12) & 7) + 8); break;
                    case 7: if (rend) IncX(); break;
                }
            }
            if (cycle == 256 && rend) IncY();
            if (cycle == 257)
            {
                LoadBg();
                if (rend) v = (v & ~0x041F) | (t & 0x041F);
                if (scanline >= 0) EvalSprites(); else sCount = 0;
            }
            if (cycle == 338 || cycle == 340) nId = Rd(0x2000 | (v & 0x0FFF));
            if (scanline == -1 && cycle >= 280 && cycle < 305 && rend) v = (v & ~0x7BE0) | (t & 0x7BE0);
            if (cycle == 340) FetchSprites();
        }

        if (scanline == 241 && cycle == 1)
        {
            status |= 0x80;
            if ((ctrl & 0x80) != 0) Nmi = true;
        }

        if (scanline >= 0 && scanline < 240 && cycle >= 1 && cycle <= 256) Pixel();

        if (++cycle >= 341)
        {
            cycle = 0;
            if (++scanline >= 261) { scanline = -1; FrameDone = true; odd = !odd; }
        }
    }

    void Pixel()
    {
        int bgPix = 0, bgPal = 0, fgPix = 0, fgPal = 0;
        bool fgPrio = false;
        if ((mask & 8) != 0 && (cycle >= 9 || (mask & 2) != 0))
        {
            int bit = 0x8000 >> fineX;
            bgPix = (((bgHi & bit) != 0 ? 1 : 0) << 1) | ((bgLo & bit) != 0 ? 1 : 0);
            bgPal = (((bgAtHi & bit) != 0 ? 1 : 0) << 1) | ((bgAtLo & bit) != 0 ? 1 : 0);
        }
        spr0Rendering = false;
        if ((mask & 0x10) != 0 && (cycle >= 9 || (mask & 4) != 0))
        {
            for (int i = 0; i < sCount; i++)
            {
                if (sX[i] != 0) continue;
                fgPix = (((sHi[i] & 0x80) != 0 ? 1 : 0) << 1) | ((sLo[i] & 0x80) != 0 ? 1 : 0);
                fgPal = (sAttr[i] & 3) + 4;
                fgPrio = (sAttr[i] & 0x20) == 0;
                if (fgPix != 0) { if (i == 0) spr0Rendering = true; break; }
            }
        }
        int pix, pl;
        if (bgPix == 0 && fgPix == 0) { pix = 0; pl = 0; }
        else if (bgPix == 0) { pix = fgPix; pl = fgPal; }
        else if (fgPix == 0) { pix = bgPix; pl = bgPal; }
        else
        {
            if (fgPrio) { pix = fgPix; pl = fgPal; } else { pix = bgPix; pl = bgPal; }
            if (spr0Possible && spr0Rendering && (mask & 0x18) == 0x18 && cycle < 256 && (cycle >= 9 || (mask & 6) == 6))
                status |= 0x40;
        }
        Frame[scanline * 256 + cycle - 1] = Pal[Rd(0x3F00 + (pl << 2) + pix) & 0x3F];
    }
}
