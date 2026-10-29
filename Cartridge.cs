public enum Mirror { Horizontal, Vertical, Single0, Single1 }

/// iNES loader + mappers 0 (NROM), 1 (MMC1), 2 (UxROM), 3 (CNROM)
public class Cartridge
{
    readonly byte[] prg, chr, ram = new byte[0x2000];
    readonly bool chrRam;
    readonly int mapper;
    readonly Mirror hard;
    int shiftReg = 0x10, ctrl = 0x0C, chr0, chr1, prgBank, bank;

    public Mirror Mirroring => mapper == 1
        ? (ctrl & 3) switch { 0 => Mirror.Single0, 1 => Mirror.Single1, 2 => Mirror.Vertical, _ => Mirror.Horizontal }
        : hard;

    public Cartridge(string path)
    {
        var d = File.ReadAllBytes(path);
        if (d.Length < 16 || d[0] != 'N' || d[1] != 'E' || d[2] != 'S' || d[3] != 0x1A)
            throw new InvalidDataException("Not an iNES file");
        int prgN = d[4], chrN = d[5], f6 = d[6], f7 = d[7];
        mapper = (f7 & 0xF0) | (f6 >> 4);
        if (mapper > 3) throw new NotSupportedException($"Mapper {mapper} not supported");
        hard = (f6 & 1) != 0 ? Mirror.Vertical : Mirror.Horizontal;
        int off = 16 + ((f6 & 4) != 0 ? 512 : 0);
        prg = d[off..(off + prgN * 0x4000)];
        off += prgN * 0x4000;
        if (chrN == 0) { chr = new byte[0x2000]; chrRam = true; }
        else chr = d[off..(off + chrN * 0x2000)];
    }

    int PrgIndex(int a)
    {
        int n = prg.Length, o = a & 0x3FFF;
        switch (mapper)
        {
            case 1:
                switch ((ctrl >> 2) & 3)
                {
                    case 0: case 1: return ((prgBank & 0xE) * 0x4000 + (a & 0x7FFF)) % n;
                    case 2: return a < 0xC000 ? o : (prgBank * 0x4000 + o) % n;
                    default: return a < 0xC000 ? (prgBank * 0x4000 + o) % n : n - 0x4000 + o;
                }
            case 2: return a < 0xC000 ? (bank * 0x4000 + o) % n : n - 0x4000 + o;
            default: return (a - 0x8000) % n;
        }
    }

    int ChrIndex(int a)
    {
        int n = chr.Length;
        switch (mapper)
        {
            case 1:
                if ((ctrl & 0x10) == 0) return ((chr0 & 0x1E) * 0x1000 + a) % n;
                return a < 0x1000 ? (chr0 * 0x1000 + a) % n : (chr1 * 0x1000 + (a & 0xFFF)) % n;
            case 3: return (bank * 0x2000 + a) % n;
            default: return a;
        }
    }

    public byte CpuRead(ushort a) => a >= 0x8000 ? prg[PrgIndex(a)] : a >= 0x6000 ? ram[a - 0x6000] : (byte)0;

    public void CpuWrite(ushort a, byte v)
    {
        if (a >= 0x8000)
        {
            switch (mapper)
            {
                case 1: Mmc1(a, v); break;
                case 2: bank = v & 0xF; break;
                case 3: bank = v & 3; break;
            }
        }
        else if (a >= 0x6000) ram[a - 0x6000] = v;
    }

    void Mmc1(int a, byte v)
    {
        if ((v & 0x80) != 0) { shiftReg = 0x10; ctrl |= 0x0C; return; }
        bool last = (shiftReg & 1) != 0;
        shiftReg = (shiftReg >> 1) | ((v & 1) << 4);
        if (!last) return;
        switch ((a >> 13) & 3)
        {
            case 0: ctrl = shiftReg; break;
            case 1: chr0 = shiftReg; break;
            case 2: chr1 = shiftReg; break;
            case 3: prgBank = shiftReg & 0xF; break;
        }
        shiftReg = 0x10;
    }

    public byte PpuRead(ushort a) => chr[ChrIndex(a)];
    public void PpuWrite(ushort a, byte v) { if (chrRam) chr[ChrIndex(a)] = v; }
}
