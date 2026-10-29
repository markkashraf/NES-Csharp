public class Nes
{
    public readonly Cartridge Cart;
    public readonly Cpu Cpu;
    public readonly Ppu Ppu;
    readonly byte[] ram = new byte[2048];
    public byte Buttons;          // A,B,Select,Start,Up,Down,Left,Right = bits 0..7
    byte shift; bool strobe; int stall;

    public Nes(Cartridge c) { Cart = c; Ppu = new Ppu(c); Cpu = new Cpu(this); Cpu.Reset(); }

    public byte CpuRead(ushort a)
    {
        if (a < 0x2000) return ram[a & 0x7FF];
        if (a < 0x4000) return Ppu.RegRead(a & 7);
        if (a == 0x4016)
        {
            if (strobe) shift = Buttons;
            byte r = (byte)(shift & 1);
            shift = (byte)((shift >> 1) | 0x80);
            return (byte)(r | 0x40);
        }
        if (a < 0x4020) return 0;
        return Cart.CpuRead(a);
    }

    public void CpuWrite(ushort a, byte v)
    {
        if (a < 0x2000) ram[a & 0x7FF] = v;
        else if (a < 0x4000) Ppu.RegWrite(a & 7, v);
        else if (a == 0x4014)
        {
            for (int i = 0; i < 256; i++) Ppu.Oam[(Ppu.OamAddr + i) & 0xFF] = CpuRead((ushort)((v << 8) + i));
            stall += 513;
        }
        else if (a == 0x4016) { strobe = (v & 1) != 0; if (strobe) shift = Buttons; }
        else if (a >= 0x4020) Cart.CpuWrite(a, v);
    }

    public void RunFrame()
    {
        Ppu.FrameDone = false;
        while (!Ppu.FrameDone)
        {
            int c = Cpu.Step() + stall; stall = 0;
            for (int i = 0; i < c * 3; i++) Ppu.Clock();
            if (Ppu.Nmi)
            {
                Ppu.Nmi = false;
                for (int i = 0, n = Cpu.Nmi() * 3; i < n; i++) Ppu.Clock();
            }
        }
    }
}
