using System.Numerics;
using System.Runtime.CompilerServices;
using Boymulator.Core.Apu;
using Boymulator.Core.Common;
using Boymulator.Core.Memory;
using Boymulator.Core.Ppu;
using Boymulator.Core.Timer;

namespace Boymulator.Core.Cpu;

public sealed class Cpu
{
    private readonly Mmu _mmu;
    private readonly InterruptController _interrupts;
    private readonly TimerController _timer;
    private readonly Ppu.Ppu _ppu;
    private readonly Apu.Apu _apu;

    private int _instructionCycles;

    public Registers Regs;
    public EmulationMode Mode { get; set; } = EmulationMode.Dmg;

    public Cpu(Mmu mmu, InterruptController interrupts, TimerController timer, Ppu.Ppu ppu, Apu.Apu apu)
    {
        _mmu = mmu;
        _interrupts = interrupts;
        _timer = timer;
        _ppu = ppu;
        _apu = apu;
        Reset();
    }

    public void Reset()
    {
        Regs = default;
        _interrupts.Reset();

        // Standard Post-boot ROM state
        if (Mode == EmulationMode.Cgb)
        {
            Regs.A = 0x11; // CGB indicator
            Regs.F = 0x80;
            Regs.B = 0x00;
            Regs.C = 0x00;
            Regs.D = 0xFF;
            Regs.E = 0x56;
            Regs.H = 0x00;
            Regs.L = 0x0D;
            Regs.SP = 0xFFFE;
            Regs.PC = 0x0100;
        }
        else
        {
            Regs.A = 0x01; // DMG indicator
            Regs.F = 0xB0;
            Regs.B = 0x00;
            Regs.C = 0x13;
            Regs.D = 0x00;
            Regs.E = 0xD8;
            Regs.H = 0x01;
            Regs.L = 0x4D;
            Regs.SP = 0xFFFE;
            Regs.PC = 0x0100;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Tick(int cpuCycles)
    {
        _instructionCycles += cpuCycles;
        int standardCycles = _mmu.DoubleSpeed ? (cpuCycles >> 1) : cpuCycles;
        _timer.Tick(cpuCycles);
        _ppu.Tick(standardCycles);
        _apu.Tick(standardCycles);
        _mmu.Cartridge?.Tick(standardCycles);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Step()
    {
        _instructionCycles = 0;

        // 1. Handle delayed IME enabling from EI instruction
        if (_interrupts.EnableImePending)
        {
            _interrupts.IME = true;
            _interrupts.EnableImePending = false;
        }

        // 2. Check Interrupts
        byte pending = (byte)(_interrupts.IE & _interrupts.IF & 0x1F);
        if (pending != 0)
        {
            if (_interrupts.Halted)
            {
                _interrupts.Halted = false;
            }

            if (_interrupts.IME)
            {
                _interrupts.IME = false;

                // 2 wait cycles (8 T-cycles)
                Tick(8);

                // Push PC to stack (1 internal + 2 writes = 12 T-cycles)
                Push(Regs.PC);

                // Service highest priority interrupt
                if ((pending & InterruptController.VBlankMask) != 0)
                {
                    _interrupts.ClearInterrupt(InterruptController.VBlankMask);
                    Regs.PC = 0x0040;
                }
                else if ((pending & InterruptController.LcdStatMask) != 0)
                {
                    _interrupts.ClearInterrupt(InterruptController.LcdStatMask);
                    Regs.PC = 0x0048;
                }
                else if ((pending & InterruptController.TimerMask) != 0)
                {
                    _interrupts.ClearInterrupt(InterruptController.TimerMask);
                    Regs.PC = 0x0050;
                }
                else if ((pending & InterruptController.SerialMask) != 0)
                {
                    _interrupts.ClearInterrupt(InterruptController.SerialMask);
                    Regs.PC = 0x0058;
                }
                else if ((pending & InterruptController.JoypadMask) != 0)
                {
                    _interrupts.ClearInterrupt(InterruptController.JoypadMask);
                    Regs.PC = 0x0060;
                }

                return _instructionCycles; // 20 T-cycles
            }
        }

        // 3. If still halted, CPU sleeps
        if (_interrupts.Halted)
        {
            Tick(4);
            return 4; // 1 M-cycle
        }

        // 4. Fetch Opcode (handling HALT bug if present)
        byte opcode = FetchByte();
        if (_interrupts.HaltBug)
        {
            Regs.PC--;
            _interrupts.HaltBug = false;
        }

        // 5. Execute Opcode
        int totalCycles = ExecuteOpcode(opcode);
        int remaining = totalCycles - _instructionCycles;
        if (remaining > 0)
        {
            Tick(remaining);
        }

        return _instructionCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte FetchByte()
    {
        byte val = _mmu.Read(Regs.PC);
        Regs.PC++;
        Tick(4);
        return val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort FetchWord()
    {
        byte low = FetchByte();
        byte high = FetchByte();
        return (ushort)((high << 8) | low);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Read(ushort addr)
    {
        byte val = _mmu.Read(addr);
        Tick(4);
        return val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Write(ushort addr, byte val)
    {
        _mmu.Write(addr, val);
        Tick(4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Push(ushort value)
    {
        Tick(4);
        Regs.SP -= 2;
        Write((ushort)(Regs.SP + 1), (byte)(value >> 8));
        Write(Regs.SP, (byte)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort Pop()
    {
        byte low = Read(Regs.SP);
        byte high = Read((ushort)(Regs.SP + 1));
        Regs.SP += 2;
        return (ushort)((high << 8) | low);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int ExecuteOpcode(byte opcode)
    {
        switch (opcode)
        {
            // 0x00 - 0x0F
            case 0x00: return 4; // NOP
            case 0x01: Regs.BC = FetchWord(); return 12; // LD BC, nn
            case 0x02: Write(Regs.BC, Regs.A); return 8; // LD (BC), A
            case 0x03: Regs.BC++; return 8; // INC BC
            case 0x04: Regs.B = Inc(Regs.B); return 4; // INC B
            case 0x05: Regs.B = Dec(Regs.B); return 4; // DEC B
            case 0x06: Regs.B = FetchByte(); return 8; // LD B, n
            case 0x07: Rlca(); return 4; // RLCA
            case 0x08: // LD (nn), SP
            {
                ushort addr = FetchWord();
                Write(addr, (byte)Regs.SP);
                Write((ushort)(addr + 1), (byte)(Regs.SP >> 8));
                return 20;
            }
            case 0x09: AddHl(Regs.BC); return 8; // ADD HL, BC
            case 0x0A: Regs.A = Read(Regs.BC); return 8; // LD A, (BC)
            case 0x0B: Regs.BC--; return 8; // DEC BC
            case 0x0C: Regs.C = Inc(Regs.C); return 4; // INC C
            case 0x0D: Regs.C = Dec(Regs.C); return 4; // DEC C
            case 0x0E: Regs.C = FetchByte(); return 8; // LD C, n
            case 0x0F: Rrca(); return 4; // RRCA

            // 0x10 - 0x1F
            case 0x10: // STOP
            {
                FetchByte(); // Skip subsequent byte
                if (Mode == EmulationMode.Cgb && _mmu.SpeedSwitchArmed)
                {
                    _mmu.DoubleSpeed = !_mmu.DoubleSpeed;
                    _mmu.SpeedSwitchArmed = false;
                }
                return 4;
            }
            case 0x11: Regs.DE = FetchWord(); return 12; // LD DE, nn
            case 0x12: Write(Regs.DE, Regs.A); return 8; // LD (DE), A
            case 0x13: Regs.DE++; return 8; // INC DE
            case 0x14: Regs.D = Inc(Regs.D); return 4; // INC D
            case 0x15: Regs.D = Dec(Regs.D); return 4; // DEC D
            case 0x16: Regs.D = FetchByte(); return 8; // LD D, n
            case 0x17: Rla(); return 4; // RLA
            case 0x18: // JR n
            {
                sbyte offset = (sbyte)FetchByte();
                Regs.PC = (ushort)(Regs.PC + offset);
                return 12;
            }
            case 0x19: AddHl(Regs.DE); return 8; // ADD HL, DE
            case 0x1A: Regs.A = Read(Regs.DE); return 8; // LD A, (DE)
            case 0x1B: Regs.DE--; return 8; // DEC DE
            case 0x1C: Regs.E = Inc(Regs.E); return 4; // INC E
            case 0x1D: Regs.E = Dec(Regs.E); return 4; // DEC E
            case 0x1E: Regs.E = FetchByte(); return 8; // LD E, n
            case 0x1F: Rra(); return 4; // RRA

            // 0x20 - 0x2F
            case 0x20: // JR NZ, n
            {
                sbyte offset = (sbyte)FetchByte();
                if (!Regs.FlagZ)
                {
                    Regs.PC = (ushort)(Regs.PC + offset);
                    return 12;
                }
                return 8;
            }
            case 0x21: Regs.HL = FetchWord(); return 12; // LD HL, nn
            case 0x22: Write(Regs.HL, Regs.A); Regs.HL++; return 8; // LD (HL+), A
            case 0x23: Regs.HL++; return 8; // INC HL
            case 0x24: Regs.H = Inc(Regs.H); return 4; // INC H
            case 0x25: Regs.H = Dec(Regs.H); return 4; // DEC H
            case 0x26: Regs.H = FetchByte(); return 8; // LD H, n
            case 0x27: Daa(); return 4; // DAA
            case 0x28: // JR Z, n
            {
                sbyte offset = (sbyte)FetchByte();
                if (Regs.FlagZ)
                {
                    Regs.PC = (ushort)(Regs.PC + offset);
                    return 12;
                }
                return 8;
            }
            case 0x29: AddHl(Regs.HL); return 8; // ADD HL, HL
            case 0x2A: Regs.A = Read(Regs.HL); Regs.HL++; return 8; // LD A, (HL+)
            case 0x2B: Regs.HL--; return 8; // DEC HL
            case 0x2C: Regs.L = Inc(Regs.L); return 4; // INC L
            case 0x2D: Regs.L = Dec(Regs.L); return 4; // DEC L
            case 0x2E: Regs.L = FetchByte(); return 8; // LD L, n
            case 0x2F: // CPL
                Regs.A = (byte)~Regs.A;
                Regs.FlagN = true;
                Regs.FlagH = true;
                return 4;

            // 0x30 - 0x3F
            case 0x30: // JR NC, n
            {
                sbyte offset = (sbyte)FetchByte();
                if (!Regs.FlagC)
                {
                    Regs.PC = (ushort)(Regs.PC + offset);
                    return 12;
                }
                return 8;
            }
            case 0x31: Regs.SP = FetchWord(); return 12; // LD SP, nn
            case 0x32: Write(Regs.HL, Regs.A); Regs.HL--; return 8; // LD (HL-), A
            case 0x33: Regs.SP++; return 8; // INC SP
            case 0x34: // INC (HL)
            {
                byte val = Read(Regs.HL);
                Write(Regs.HL, Inc(val));
                return 12;
            }
            case 0x35: // DEC (HL)
            {
                byte val = Read(Regs.HL);
                Write(Regs.HL, Dec(val));
                return 12;
            }
            case 0x36:
            {
                byte val = FetchByte();
                Write(Regs.HL, val);
                return 12; // LD (HL), n
            }
            case 0x37: // SCF
                Regs.FlagN = false;
                Regs.FlagH = false;
                Regs.FlagC = true;
                return 4;
            case 0x38: // JR C, n
            {
                sbyte offset = (sbyte)FetchByte();
                if (Regs.FlagC)
                {
                    Regs.PC = (ushort)(Regs.PC + offset);
                    return 12;
                }
                return 8;
            }
            case 0x39: AddHl(Regs.SP); return 8; // ADD HL, SP
            case 0x3A: Regs.A = Read(Regs.HL); Regs.HL--; return 8; // LD A, (HL-)
            case 0x3B: Regs.SP--; return 8; // DEC SP
            case 0x3C: Regs.A = Inc(Regs.A); return 4; // INC A
            case 0x3D: Regs.A = Dec(Regs.A); return 4; // DEC A
            case 0x3E: Regs.A = FetchByte(); return 8; // LD A, n
            case 0x3F: // CCF
                Regs.FlagN = false;
                Regs.FlagH = false;
                Regs.FlagC = !Regs.FlagC;
                return 4;

            // 0x40 - 0x7F: LD r, r' and HALT
            case 0x40: return 4; // LD B, B
            case 0x41: Regs.B = Regs.C; return 4;
            case 0x42: Regs.B = Regs.D; return 4;
            case 0x43: Regs.B = Regs.E; return 4;
            case 0x44: Regs.B = Regs.H; return 4;
            case 0x45: Regs.B = Regs.L; return 4;
            case 0x46: Regs.B = Read(Regs.HL); return 8;
            case 0x47: Regs.B = Regs.A; return 4;

            case 0x48: Regs.C = Regs.B; return 4;
            case 0x49: return 4; // LD C, C
            case 0x4A: Regs.C = Regs.D; return 4;
            case 0x4B: Regs.C = Regs.E; return 4;
            case 0x4C: Regs.C = Regs.H; return 4;
            case 0x4D: Regs.C = Regs.L; return 4;
            case 0x4E: Regs.C = Read(Regs.HL); return 8;
            case 0x4F: Regs.C = Regs.A; return 4;

            case 0x50: Regs.D = Regs.B; return 4;
            case 0x51: Regs.D = Regs.C; return 4;
            case 0x52: return 4; // LD D, D
            case 0x53: Regs.D = Regs.E; return 4;
            case 0x54: Regs.D = Regs.H; return 4;
            case 0x55: Regs.D = Regs.L; return 4;
            case 0x56: Regs.D = Read(Regs.HL); return 8;
            case 0x57: Regs.D = Regs.A; return 4;

            case 0x58: Regs.E = Regs.B; return 4;
            case 0x59: Regs.E = Regs.C; return 4;
            case 0x5A: Regs.E = Regs.D; return 4;
            case 0x5B: return 4; // LD E, E
            case 0x5C: Regs.E = Regs.H; return 4;
            case 0x5D: Regs.E = Regs.L; return 4;
            case 0x5E: Regs.E = Read(Regs.HL); return 8;
            case 0x5F: Regs.E = Regs.A; return 4;

            case 0x60: Regs.H = Regs.B; return 4;
            case 0x61: Regs.H = Regs.C; return 4;
            case 0x62: Regs.H = Regs.D; return 4;
            case 0x63: Regs.H = Regs.E; return 4;
            case 0x64: return 4; // LD H, H
            case 0x65: Regs.H = Regs.L; return 4;
            case 0x66: Regs.H = Read(Regs.HL); return 8;
            case 0x67: Regs.H = Regs.A; return 4;

            case 0x68: Regs.L = Regs.B; return 4;
            case 0x69: Regs.L = Regs.C; return 4;
            case 0x6A: Regs.L = Regs.D; return 4;
            case 0x6B: Regs.L = Regs.E; return 4;
            case 0x6C: Regs.L = Regs.H; return 4;
            case 0x6D: return 4; // LD L, L
            case 0x6E: Regs.L = Read(Regs.HL); return 8;
            case 0x6F: Regs.L = Regs.A; return 4;

            case 0x70: Write(Regs.HL, Regs.B); return 8;
            case 0x71: Write(Regs.HL, Regs.C); return 8;
            case 0x72: Write(Regs.HL, Regs.D); return 8;
            case 0x73: Write(Regs.HL, Regs.E); return 8;
            case 0x74: Write(Regs.HL, Regs.H); return 8;
            case 0x75: Write(Regs.HL, Regs.L); return 8;
            case 0x76: // HALT
            {
                if (!_interrupts.IME && ((_interrupts.IE & _interrupts.IF & 0x1F) != 0))
                {
                    _interrupts.HaltBug = true;
                }
                else
                {
                    _interrupts.Halted = true;
                }
                return 4;
            }
            case 0x77: Write(Regs.HL, Regs.A); return 8;

            case 0x78: Regs.A = Regs.B; return 4;
            case 0x79: Regs.A = Regs.C; return 4;
            case 0x7A: Regs.A = Regs.D; return 4;
            case 0x7B: Regs.A = Regs.E; return 4;
            case 0x7C: Regs.A = Regs.H; return 4;
            case 0x7D: Regs.A = Regs.L; return 4;
            case 0x7E: Regs.A = Read(Regs.HL); return 8;
            case 0x7F: return 4; // LD A, A

            // 0x80 - 0x87: ADD A, r
            case 0x80: Add(Regs.B); return 4;
            case 0x81: Add(Regs.C); return 4;
            case 0x82: Add(Regs.D); return 4;
            case 0x83: Add(Regs.E); return 4;
            case 0x84: Add(Regs.H); return 4;
            case 0x85: Add(Regs.L); return 4;
            case 0x86: Add(Read(Regs.HL)); return 8;
            case 0x87: Add(Regs.A); return 4;

            // 0x88 - 0x8F: ADC A, r
            case 0x88: Adc(Regs.B); return 4;
            case 0x89: Adc(Regs.C); return 4;
            case 0x8A: Adc(Regs.D); return 4;
            case 0x8B: Adc(Regs.E); return 4;
            case 0x8C: Adc(Regs.H); return 4;
            case 0x8D: Adc(Regs.L); return 4;
            case 0x8E: Adc(Read(Regs.HL)); return 8;
            case 0x8F: Adc(Regs.A); return 4;

            // 0x90 - 0x97: SUB r
            case 0x90: Sub(Regs.B); return 4;
            case 0x91: Sub(Regs.C); return 4;
            case 0x92: Sub(Regs.D); return 4;
            case 0x93: Sub(Regs.E); return 4;
            case 0x94: Sub(Regs.H); return 4;
            case 0x95: Sub(Regs.L); return 4;
            case 0x96: Sub(Read(Regs.HL)); return 8;
            case 0x97: Sub(Regs.A); return 4;

            // 0x98 - 0x9F: SBC A, r
            case 0x98: Sbc(Regs.B); return 4;
            case 0x99: Sbc(Regs.C); return 4;
            case 0x9A: Sbc(Regs.D); return 4;
            case 0x9B: Sbc(Regs.E); return 4;
            case 0x9C: Sbc(Regs.H); return 4;
            case 0x9D: Sbc(Regs.L); return 4;
            case 0x9E: Sbc(Read(Regs.HL)); return 8;
            case 0x9F: Sbc(Regs.A); return 4;

            // 0xA0 - 0xA7: AND r
            case 0xA0: And(Regs.B); return 4;
            case 0xA1: And(Regs.C); return 4;
            case 0xA2: And(Regs.D); return 4;
            case 0xA3: And(Regs.E); return 4;
            case 0xA4: And(Regs.H); return 4;
            case 0xA5: And(Regs.L); return 4;
            case 0xA6: And(Read(Regs.HL)); return 8;
            case 0xA7: And(Regs.A); return 4;

            // 0xA8 - 0xAF: XOR r
            case 0xA8: Xor(Regs.B); return 4;
            case 0xA9: Xor(Regs.C); return 4;
            case 0xAA: Xor(Regs.D); return 4;
            case 0xAB: Xor(Regs.E); return 4;
            case 0xAC: Xor(Regs.H); return 4;
            case 0xAD: Xor(Regs.L); return 4;
            case 0xAE: Xor(Read(Regs.HL)); return 8;
            case 0xAF: Xor(Regs.A); return 4;

            // 0xB0 - 0xB7: OR r
            case 0xB0: Or(Regs.B); return 4;
            case 0xB1: Or(Regs.C); return 4;
            case 0xB2: Or(Regs.D); return 4;
            case 0xB3: Or(Regs.E); return 4;
            case 0xB4: Or(Regs.H); return 4;
            case 0xB5: Or(Regs.L); return 4;
            case 0xB6: Or(Read(Regs.HL)); return 8;
            case 0xB7: Or(Regs.A); return 4;

            // 0xB8 - 0xBF: CP r
            case 0xB8: Cp(Regs.B); return 4;
            case 0xB9: Cp(Regs.C); return 4;
            case 0xBA: Cp(Regs.D); return 4;
            case 0xBB: Cp(Regs.E); return 4;
            case 0xBC: Cp(Regs.H); return 4;
            case 0xBD: Cp(Regs.L); return 4;
            case 0xBE: Cp(Read(Regs.HL)); return 8;
            case 0xBF: Cp(Regs.A); return 4;

            // 0xC0 - 0xCF
            case 0xC0: // RET NZ
                if (!Regs.FlagZ) { Regs.PC = Pop(); return 20; }
                return 8;
            case 0xC1: Regs.BC = Pop(); return 12; // POP BC
            case 0xC2: // JP NZ, nn
            {
                ushort addr = FetchWord();
                if (!Regs.FlagZ) { Regs.PC = addr; return 16; }
                return 12;
            }
            case 0xC3: Regs.PC = FetchWord(); return 16; // JP nn
            case 0xC4: // CALL NZ, nn
            {
                ushort addr = FetchWord();
                if (!Regs.FlagZ) { Push(Regs.PC); Regs.PC = addr; return 24; }
                return 12;
            }
            case 0xC5: Push(Regs.BC); return 16; // PUSH BC
            case 0xC6: Add(FetchByte()); return 8; // ADD A, n
            case 0xC7: Push(Regs.PC); Regs.PC = 0x0000; return 16; // RST 00H
            case 0xC8: // RET Z
                if (Regs.FlagZ) { Regs.PC = Pop(); return 20; }
                return 8;
            case 0xC9: Regs.PC = Pop(); return 16; // RET
            case 0xCA: // JP Z, nn
            {
                ushort addr = FetchWord();
                if (Regs.FlagZ) { Regs.PC = addr; return 16; }
                return 12;
            }
            case 0xCB: return ExecuteCb(FetchByte()); // PREFIX CB
            case 0xCC: // CALL Z, nn
            {
                ushort addr = FetchWord();
                if (Regs.FlagZ) { Push(Regs.PC); Regs.PC = addr; return 24; }
                return 12;
            }
            case 0xCD: // CALL nn
            {
                ushort addr = FetchWord();
                Push(Regs.PC);
                Regs.PC = addr;
                return 24;
            }
            case 0xCE: Adc(FetchByte()); return 8; // ADC A, n
            case 0xCF: Push(Regs.PC); Regs.PC = 0x0008; return 16; // RST 08H

            // 0xD0 - 0xDF
            case 0xD0: // RET NC
                if (!Regs.FlagC) { Regs.PC = Pop(); return 20; }
                return 8;
            case 0xD1: Regs.DE = Pop(); return 12; // POP DE
            case 0xD2: // JP NC, nn
            {
                ushort addr = FetchWord();
                if (!Regs.FlagC) { Regs.PC = addr; return 16; }
                return 12;
            }
            case 0xD4: // CALL NC, nn
            {
                ushort addr = FetchWord();
                if (!Regs.FlagC) { Push(Regs.PC); Regs.PC = addr; return 24; }
                return 12;
            }
            case 0xD5: Push(Regs.DE); return 16; // PUSH DE
            case 0xD6: Sub(FetchByte()); return 8; // SUB n
            case 0xD7: Push(Regs.PC); Regs.PC = 0x0010; return 16; // RST 10H
            case 0xD8: // RET C
                if (Regs.FlagC) { Regs.PC = Pop(); return 20; }
                return 8;
            case 0xD9: // RETI
                Regs.PC = Pop();
                _interrupts.IME = true;
                return 16;
            case 0xDA: // JP C, nn
            {
                ushort addr = FetchWord();
                if (Regs.FlagC) { Regs.PC = addr; return 16; }
                return 12;
            }
            case 0xDC: // CALL C, nn
            {
                ushort addr = FetchWord();
                if (Regs.FlagC) { Push(Regs.PC); Regs.PC = addr; return 24; }
                return 12;
            }
            case 0xDE: Sbc(FetchByte()); return 8; // SBC A, n
            case 0xDF: Push(Regs.PC); Regs.PC = 0x0018; return 16; // RST 18H

            // 0xE0 - 0xEF
            case 0xE0: // LDH (n), A
            {
                ushort addr = (ushort)(0xFF00 | FetchByte());
                Write(addr, Regs.A);
                return 12;
            }
            case 0xE1: Regs.HL = Pop(); return 12; // POP HL
            case 0xE2: Write((ushort)(0xFF00 | Regs.C), Regs.A); return 8; // LD (C), A
            case 0xE5: Push(Regs.HL); return 16; // PUSH HL
            case 0xE6: And(FetchByte()); return 8; // AND n
            case 0xE7: Push(Regs.PC); Regs.PC = 0x0020; return 16; // RST 20H
            case 0xE8: // ADD SP, e
            {
                sbyte offset = (sbyte)FetchByte();
                int result = Regs.SP + offset;
                Regs.SetFlags(
                    false,
                    false,
                    ((Regs.SP ^ offset ^ (result & 0xFFFF)) & 0x10) != 0,
                    ((Regs.SP ^ offset ^ (result & 0xFFFF)) & 0x100) != 0
                );
                Regs.SP = (ushort)result;
                return 16;
            }
            case 0xE9: Regs.PC = Regs.HL; return 4; // JP (HL)
            case 0xEA:
            {
                ushort addr = FetchWord();
                Write(addr, Regs.A);
                return 16; // LD (nn), A
            }
            case 0xEE: Xor(FetchByte()); return 8; // XOR n
            case 0xEF: Push(Regs.PC); Regs.PC = 0x0028; return 16; // RST 28H

            // 0xF0 - 0xFF
            case 0xF0: // LDH A, (n)
            {
                ushort addr = (ushort)(0xFF00 | FetchByte());
                Regs.A = Read(addr);
                return 12;
            }
            case 0xF1: Regs.AF = Pop(); return 12; // POP AF
            case 0xF2: Regs.A = Read((ushort)(0xFF00 | Regs.C)); return 8; // LD A, (C)
            case 0xF3: // DI
                _interrupts.IME = false;
                _interrupts.EnableImePending = false;
                return 4;
            case 0xF5: Push(Regs.AF); return 16; // PUSH AF
            case 0xF6: Or(FetchByte()); return 8; // OR n
            case 0xF7: Push(Regs.PC); Regs.PC = 0x0030; return 16; // RST 30H
            case 0xF8: // LD HL, SP+e
            {
                sbyte offset = (sbyte)FetchByte();
                int result = Regs.SP + offset;
                Regs.SetFlags(
                    false,
                    false,
                    ((Regs.SP ^ offset ^ (result & 0xFFFF)) & 0x10) != 0,
                    ((Regs.SP ^ offset ^ (result & 0xFFFF)) & 0x100) != 0
                );
                Regs.HL = (ushort)result;
                return 12;
            }
            case 0xF9: Regs.SP = Regs.HL; return 8; // LD SP, HL
            case 0xFA:
            {
                ushort addr = FetchWord();
                Regs.A = Read(addr);
                return 16; // LD A, (nn)
            }
            case 0xFB: // EI
                _interrupts.EnableImePending = true;
                return 4;
            case 0xFE: Cp(FetchByte()); return 8; // CP n
            case 0xFF: Push(Regs.PC); Regs.PC = 0x0038; return 16; // RST 38H

            default: return 4;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int ExecuteCb(byte cbOpcode)
    {
        int reg = cbOpcode & 0x07;
        byte val = GetCbVal(reg);
        int bit = (cbOpcode >> 3) & 0x07;

        switch (cbOpcode >> 6)
        {
            case 0: // Shifts / Rotates
            {
                byte result = (cbOpcode >> 3) switch
                {
                    0 => Rlc(val),
                    1 => Rrc(val),
                    2 => Rl(val),
                    3 => Rr(val),
                    4 => Sla(val),
                    5 => Sra(val),
                    6 => Swap(val),
                    7 => Srl(val),
                    _ => val
                };
                SetCbVal(reg, result);
                return reg == 6 ? 16 : 8;
            }

            case 1: // BIT
                Bit(bit, val);
                return reg == 6 ? 12 : 8;

            case 2: // RES
                val = (byte)(val & ~(1 << bit));
                SetCbVal(reg, val);
                return reg == 6 ? 16 : 8;

            case 3: // SET
                val = (byte)(val | (1 << bit));
                SetCbVal(reg, val);
                return reg == 6 ? 16 : 8;

            default:
                return 8;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte GetCbVal(int index) => index switch
    {
        0 => Regs.B,
        1 => Regs.C,
        2 => Regs.D,
        3 => Regs.E,
        4 => Regs.H,
        5 => Regs.L,
        6 => Read(Regs.HL),
        7 => Regs.A,
        _ => 0
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetCbVal(int index, byte val)
    {
        switch (index)
        {
            case 0: Regs.B = val; break;
            case 1: Regs.C = val; break;
            case 2: Regs.D = val; break;
            case 3: Regs.E = val; break;
            case 4: Regs.H = val; break;
            case 5: Regs.L = val; break;
            case 6: Write(Regs.HL, val); break;
            case 7: Regs.A = val; break;
        }
    }

    // --- ALU Helpers ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Add(byte value)
    {
        int result = Regs.A + value;
        Regs.SetFlags(
            (byte)result == 0,
            false,
            ((Regs.A & 0x0F) + (value & 0x0F)) > 0x0F,
            result > 0xFF
        );
        Regs.A = (byte)result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Adc(byte value)
    {
        int carry = Regs.FlagC ? 1 : 0;
        int result = Regs.A + value + carry;
        Regs.SetFlags(
            (byte)result == 0,
            false,
            ((Regs.A & 0x0F) + (value & 0x0F) + carry) > 0x0F,
            result > 0xFF
        );
        Regs.A = (byte)result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Sub(byte value)
    {
        int result = Regs.A - value;
        Regs.SetFlags(
            (byte)result == 0,
            true,
            (Regs.A & 0x0F) < (value & 0x0F),
            Regs.A < value
        );
        Regs.A = (byte)result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Sbc(byte value)
    {
        int carry = Regs.FlagC ? 1 : 0;
        int result = Regs.A - value - carry;
        Regs.SetFlags(
            (byte)result == 0,
            true,
            (Regs.A & 0x0F) < (value & 0x0F) + carry,
            Regs.A < value + carry
        );
        Regs.A = (byte)result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void And(byte value)
    {
        Regs.A &= value;
        Regs.SetFlags(Regs.A == 0, false, true, false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Xor(byte value)
    {
        Regs.A ^= value;
        Regs.SetFlags(Regs.A == 0, false, false, false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Or(byte value)
    {
        Regs.A |= value;
        Regs.SetFlags(Regs.A == 0, false, false, false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Cp(byte value)
    {
        Regs.SetFlags(
            Regs.A == value,
            true,
            (Regs.A & 0x0F) < (value & 0x0F),
            Regs.A < value
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Inc(byte value)
    {
        byte result = (byte)(value + 1);
        Regs.FlagZ = result == 0;
        Regs.FlagN = false;
        Regs.FlagH = (value & 0x0F) == 0x0F;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Dec(byte value)
    {
        byte result = (byte)(value - 1);
        Regs.FlagZ = result == 0;
        Regs.FlagN = true;
        Regs.FlagH = (value & 0x0F) == 0x00;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddHl(ushort value)
    {
        int result = Regs.HL + value;
        Regs.FlagN = false;
        Regs.FlagH = ((Regs.HL & 0x0FFF) + (value & 0x0FFF)) > 0x0FFF;
        Regs.FlagC = result > 0xFFFF;
        Regs.HL = (ushort)result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Daa()
    {
        int a = Regs.A;
        if (!Regs.FlagN)
        {
            if (Regs.FlagH || (a & 0x0F) > 9) a += 0x06;
            if (Regs.FlagC || a > 0x9F) { a += 0x60; Regs.FlagC = true; }
        }
        else
        {
            if (Regs.FlagH) a = (a - 0x06) & 0xFF;
            if (Regs.FlagC) a -= 0x60;
        }

        Regs.A = (byte)a;
        Regs.FlagZ = Regs.A == 0;
        Regs.FlagH = false;
    }

    // --- Rotates & Shifts ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Rlca()
    {
        bool c = (Regs.A & 0x80) != 0;
        Regs.A = (byte)((Regs.A << 1) | (c ? 1 : 0));
        Regs.SetFlags(false, false, false, c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Rla()
    {
        bool oldC = Regs.FlagC;
        bool newC = (Regs.A & 0x80) != 0;
        Regs.A = (byte)((Regs.A << 1) | (oldC ? 1 : 0));
        Regs.SetFlags(false, false, false, newC);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Rrca()
    {
        bool c = (Regs.A & 0x01) != 0;
        Regs.A = (byte)((Regs.A >> 1) | (c ? 0x80 : 0));
        Regs.SetFlags(false, false, false, c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Rra()
    {
        bool oldC = Regs.FlagC;
        bool newC = (Regs.A & 0x01) != 0;
        Regs.A = (byte)((Regs.A >> 1) | (oldC ? 0x80 : 0));
        Regs.SetFlags(false, false, false, newC);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Rlc(byte value)
    {
        bool c = (value & 0x80) != 0;
        byte result = (byte)((value << 1) | (c ? 1 : 0));
        Regs.SetFlags(result == 0, false, false, c);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Rrc(byte value)
    {
        bool c = (value & 0x01) != 0;
        byte result = (byte)((value >> 1) | (c ? 0x80 : 0));
        Regs.SetFlags(result == 0, false, false, c);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Rl(byte value)
    {
        bool oldC = Regs.FlagC;
        bool newC = (value & 0x80) != 0;
        byte result = (byte)((value << 1) | (oldC ? 1 : 0));
        Regs.SetFlags(result == 0, false, false, newC);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Rr(byte value)
    {
        bool oldC = Regs.FlagC;
        bool newC = (value & 0x01) != 0;
        byte result = (byte)((value >> 1) | (oldC ? 0x80 : 0));
        Regs.SetFlags(result == 0, false, false, newC);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Sla(byte value)
    {
        bool c = (value & 0x80) != 0;
        byte result = (byte)(value << 1);
        Regs.SetFlags(result == 0, false, false, c);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Sra(byte value)
    {
        bool c = (value & 0x01) != 0;
        byte result = (byte)((value >> 1) | (value & 0x80));
        Regs.SetFlags(result == 0, false, false, c);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Srl(byte value)
    {
        bool c = (value & 0x01) != 0;
        byte result = (byte)(value >> 1);
        Regs.SetFlags(result == 0, false, false, c);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Swap(byte value)
    {
        byte result = (byte)(((value & 0x0F) << 4) | ((value & 0xF0) >> 4));
        Regs.SetFlags(result == 0, false, false, false);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Bit(int bit, byte value)
    {
        bool zero = (value & (1 << bit)) == 0;
        Regs.FlagZ = zero;
        Regs.FlagN = false;
        Regs.FlagH = true;
    }
}

