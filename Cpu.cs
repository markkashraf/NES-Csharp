/// MOS 6502 (official opcodes; unofficial ones act as NOPs). Decoded via aaabbbcc pattern.
public class Cpu
{
    enum M { Imp, Acc, Imm, Zp, Zpx, Zpy, Abs, Abx, Aby, Izx, Izy }
    static readonly M[] M01 = { M.Izx, M.Zp, M.Imm, M.Abs, M.Izy, M.Zpx, M.Aby, M.Abx };
    static readonly M[] M10 = { M.Imm, M.Zp, M.Acc, M.Abs, M.Imp, M.Zpx, M.Imp, M.Abx };
    static readonly M[] M00 = { M.Imm, M.Zp, M.Imp, M.Abs, M.Imp, M.Zpx, M.Imp, M.Abx };

    public byte A, X, Y, SP = 0xFD;
    public ushort PC;
    bool C, Z, I = true, D, V, N;
    readonly Nes bus;

    public Cpu(Nes b) { bus = b; }

    byte Rd(int a) => bus.CpuRead((ushort)a);
    void Wr(int a, byte v) => bus.CpuWrite((ushort)a, v);
    void Push(byte v) { Wr(0x100 + SP, v); SP--; }
    byte Pop() { SP++; return Rd(0x100 + SP); }
    void NZ(byte v) { Z = v == 0; N = (v & 0x80) != 0; }

    byte GetP(bool b) => (byte)((N ? 0x80 : 0) | (V ? 0x40 : 0) | 0x20 | (b ? 0x10 : 0) | (D ? 8 : 0) | (I ? 4 : 0) | (Z ? 2 : 0) | (C ? 1 : 0));
    void SetP(byte p) { C = (p & 1) != 0; Z = (p & 2) != 0; I = (p & 4) != 0; D = (p & 8) != 0; V = (p & 0x40) != 0; N = (p & 0x80) != 0; }

    public void Reset() { PC = (ushort)(Rd(0xFFFC) | Rd(0xFFFD) << 8); SP = 0xFD; I = true; }

    public int Nmi()
    {
        Push((byte)(PC >> 8)); Push((byte)PC); Push(GetP(false));
        I = true;
        PC = (ushort)(Rd(0xFFFA) | Rd(0xFFFB) << 8);
        return 7;
    }

    void Adc(byte v)
    {
        int s = A + v + (C ? 1 : 0);
        V = ((~(A ^ v) & (A ^ s)) & 0x80) != 0;
        C = s > 0xFF;
        A = (byte)s; NZ(A);
    }
    void Cmp(byte r, byte v) { C = r >= v; NZ((byte)(r - v)); }

    byte Rmw(int aaa, byte v)
    {
        byte r;
        switch (aaa)
        {
            case 0: C = (v & 0x80) != 0; r = (byte)(v << 1); break;
            case 1: r = (byte)((v << 1) | (C ? 1 : 0)); C = (v & 0x80) != 0; break;
            case 2: C = (v & 1) != 0; r = (byte)(v >> 1); break;
            case 3: r = (byte)((v >> 1) | (C ? 0x80 : 0)); C = (v & 1) != 0; break;
            case 6: r = (byte)(v - 1); break;
            default: r = (byte)(v + 1); break;
        }
        NZ(r); return r;
    }

    int Addr(M m, out bool cross)
    {
        cross = false; int a, b;
        switch (m)
        {
            case M.Imm: return PC++;
            case M.Zp: return Rd(PC++);
            case M.Zpx: return (byte)(Rd(PC++) + X);
            case M.Zpy: return (byte)(Rd(PC++) + Y);
            case M.Abs: a = Rd(PC) | Rd(PC + 1) << 8; PC += 2; return a;
            case M.Abx: b = Rd(PC) | Rd(PC + 1) << 8; PC += 2; a = (b + X) & 0xFFFF; cross = (a & 0xFF00) != (b & 0xFF00); return a;
            case M.Aby: b = Rd(PC) | Rd(PC + 1) << 8; PC += 2; a = (b + Y) & 0xFFFF; cross = (a & 0xFF00) != (b & 0xFF00); return a;
            case M.Izx: { int zp = (byte)(Rd(PC++) + X); return Rd(zp) | Rd((byte)(zp + 1)) << 8; }
            default: { int zp = Rd(PC++); b = Rd(zp) | Rd((byte)(zp + 1)) << 8; a = (b + Y) & 0xFFFF; cross = (a & 0xFF00) != (b & 0xFF00); return a; }
        }
    }

    static int RCyc(M m, bool x) => m switch
    {
        M.Zp => 3, M.Zpx or M.Zpy or M.Abs => 4, M.Abx or M.Aby => x ? 5 : 4, M.Izx => 6, M.Izy => x ? 6 : 5, _ => 2
    };
    static int WCyc(M m) => m switch { M.Zp => 3, M.Zpx or M.Zpy or M.Abs => 4, M.Abx or M.Aby => 5, _ => 6 };
    static int MCyc(M m) => m switch { M.Zp => 5, M.Zpx or M.Abs => 6, M.Abx => 7, _ => 2 };

    /// Executes one instruction, returns CPU cycles used.
    public int Step()
    {
        byte op = Rd(PC++);
        switch (op)
        {
            case 0x00: PC++; Push((byte)(PC >> 8)); Push((byte)PC); Push(GetP(true)); I = true; PC = (ushort)(Rd(0xFFFE) | Rd(0xFFFF) << 8); return 7;
            case 0x08: Push(GetP(true)); return 3;
            case 0x18: C = false; return 2;
            case 0x20: { int t = Rd(PC) | Rd(PC + 1) << 8; PC++; Push((byte)(PC >> 8)); Push((byte)PC); PC = (ushort)t; return 6; }
            case 0x28: SetP(Pop()); return 4;
            case 0x38: C = true; return 2;
            case 0x40: SetP(Pop()); { int lo = Pop(); int hi = Pop(); PC = (ushort)(lo | hi << 8); } return 6;
            case 0x48: Push(A); return 3;
            case 0x58: I = false; return 2;
            case 0x60: { int lo = Pop(); int hi = Pop(); PC = (ushort)((lo | hi << 8) + 1); return 6; }
            case 0x68: A = Pop(); NZ(A); return 4;
            case 0x78: I = true; return 2;
            case 0x88: Y--; NZ(Y); return 2;
            case 0x8A: A = X; NZ(A); return 2;
            case 0x98: A = Y; NZ(A); return 2;
            case 0x9A: SP = X; return 2;
            case 0xA8: Y = A; NZ(Y); return 2;
            case 0xAA: X = A; NZ(X); return 2;
            case 0xB8: V = false; return 2;
            case 0xBA: X = SP; NZ(X); return 2;
            case 0xC8: Y++; NZ(Y); return 2;
            case 0xCA: X--; NZ(X); return 2;
            case 0xD8: D = false; return 2;
            case 0xE8: X++; NZ(X); return 2;
            case 0xEA: return 2;
            case 0xF8: D = true; return 2;
            case 0x6C:
                {
                    int p = Rd(PC) | Rd(PC + 1) << 8;
                    PC = (ushort)(Rd(p) | Rd((p & 0xFF00) | ((p + 1) & 0xFF)) << 8);
                    return 5;
                }
        }

        if ((op & 0x1F) == 0x10) // branches
        {
            sbyte off = (sbyte)Rd(PC++);
            bool f = (op >> 6) switch { 0 => N, 1 => V, 2 => C, _ => Z };
            if (f != (((op >> 5) & 1) != 0)) return 2;
            ushort np = (ushort)(PC + off);
            int c = (np & 0xFF00) != (PC & 0xFF00) ? 4 : 3;
            PC = np; return c;
        }

        int cc = op & 3, aaa = op >> 5, bbb = (op >> 2) & 7;
        if (cc == 3) return 2;
        M m = cc == 1 ? M01[bbb] : cc == 2 ? M10[bbb] : M00[bbb];
        if (cc == 2)
        {
            if (m == M.Zpx && (aaa == 4 || aaa == 5)) m = M.Zpy;
            else if (m == M.Abx && aaa == 5) m = M.Aby;
        }
        if (m == M.Imp) return 2;
        if (m == M.Acc) { A = Rmw(aaa, A); return 2; }

        int addr = Addr(m, out bool cross);
        switch (cc)
        {
            case 1:
                if (aaa == 4) { if (m == M.Imm) return 2; Wr(addr, A); return WCyc(m); }
                {
                    byte v = Rd(addr);
                    switch (aaa)
                    {
                        case 0: A |= v; NZ(A); break;
                        case 1: A &= v; NZ(A); break;
                        case 2: A ^= v; NZ(A); break;
                        case 3: Adc(v); break;
                        case 5: A = v; NZ(A); break;
                        case 6: Cmp(A, v); break;
                        case 7: Adc((byte)~v); break;
                    }
                    return RCyc(m, cross);
                }
            case 2:
                if (aaa == 4) { Wr(addr, X); return WCyc(m); }
                if (aaa == 5) { X = Rd(addr); NZ(X); return RCyc(m, cross); }
                if (m == M.Imm) return 2;
                Wr(addr, Rmw(aaa, Rd(addr)));
                return MCyc(m);
            default:
                switch (aaa)
                {
                    case 1: { byte v = Rd(addr); Z = (A & v) == 0; N = (v & 0x80) != 0; V = (v & 0x40) != 0; return RCyc(m, cross); }
                    case 2: if (op == 0x4C) { PC = (ushort)addr; return 3; } break;
                    case 4: if (m == M.Imm || m == M.Abx) return 2; Wr(addr, Y); return WCyc(m);
                    case 5: Y = Rd(addr); NZ(Y); return RCyc(m, cross);
                    case 6: Cmp(Y, Rd(addr)); return RCyc(m, cross);
                    case 7: Cmp(X, Rd(addr)); return RCyc(m, cross);
                }
                return RCyc(m, cross); // NOP variants
        }
    }
}
