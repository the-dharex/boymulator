using System.Runtime.CompilerServices;
using Boymulator.Core.Apu;
using Boymulator.Core.Common;
using Boymulator.Core.Cpu;
using Boymulator.Core.Input;
using Boymulator.Core.Ppu;
using Boymulator.Core.Timer;

namespace Boymulator.Core.Memory;

public sealed class Mmu
{
    private readonly InterruptController _interrupts;
    private readonly TimerController _timer;
    private readonly Ppu.Ppu _ppu;
    private readonly Joypad _joypad;
    private readonly Apu.Apu _apu;

    public Cartridge Cartridge { get; set; } = null!;
    public EmulationMode Mode { get; set; } = EmulationMode.Dmg;

    // Internal RAM:
    // WRAM: 32KB (Bank 0 is 4KB; Banks 1-7 are 4KB each for CGB)
    public readonly byte[] Wram = new byte[8 * 0x1000];
    public int WramBank = 1; // 1-7

    // HRAM: 127 bytes (0xFF80 - 0xFFFE)
    public readonly byte[] Hram = new byte[128];

    // CGB Speed Switch (KEY1 - 0xFF4D)
    public bool DoubleSpeed;
    public bool SpeedSwitchArmed;

    // CGB HDMA registers
    public ushort HdmaSource;
    public ushort HdmaDest;
    public byte Hdma5 = 0xFF;
    public bool HdmaActive;
    public int HdmaBlocksRemaining;

    // Serial
    public byte SerialData;
    public byte SerialControl;
    public Action<byte>? OnSerialByte;

    public Mmu(InterruptController interrupts, TimerController timer, Ppu.Ppu ppu, Joypad joypad, Apu.Apu apu)
    {
        _interrupts = interrupts;
        _timer = timer;
        _ppu = ppu;
        _joypad = joypad;
        _apu = apu;

        _ppu.OnHBlank = OnPpuHBlank;
        Reset();
    }

    public void Reset()
    {
        Array.Clear(Wram);
        Array.Clear(Hram);
        _apu.Reset();
        WramBank = 1;
        DoubleSpeed = false;
        SpeedSwitchArmed = false;
        HdmaSource = 0;
        HdmaDest = 0;
        Hdma5 = 0xFF;
        HdmaActive = false;
        HdmaBlocksRemaining = 0;
        SerialData = 0;
        SerialControl = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Read(ushort address)
    {
        switch (address & 0xF000)
        {
            // Cartridge ROM (0x0000 - 0x7FFF)
            case 0x0000 or 0x1000 or 0x2000 or 0x3000 or 0x4000 or 0x5000 or 0x6000 or 0x7000:
                return Cartridge.ReadRom(address);

            // VRAM (0x8000 - 0x9FFF)
            case 0x8000 or 0x9000:
                return _ppu.Vram[(_ppu.VramBank * 0x2000) + (address & 0x1FFF)];

            // Cartridge RAM (0xA000 - 0xBFFF)
            case 0xA000 or 0xB000:
                return Cartridge.ReadRam(address);

            // WRAM Bank 0 (0xC000 - 0xCFFF)
            case 0xC000:
                return Wram[address & 0x0FFF];

            // WRAM Bank 1-7 (0xD000 - 0xDFFF)
            case 0xD000:
                return Wram[(WramBank * 0x1000) + (address & 0x0FFF)];

            // Echo RAM (0xE000 - 0xFDFF)
            case 0xE000:
                return Wram[address & 0x0FFF];
            case 0xF000:
                if (address < 0xFE00)
                {
                    return Wram[(WramBank * 0x1000) + (address & 0x0FFF)];
                }
                return ReadHigh(address);

            default:
                return 0xFF;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte ReadHigh(ushort address)
    {
        // OAM (0xFE00 - 0xFE9F)
        if (address < 0xFEA0)
        {
            return _ppu.Oam[address - 0xFE00];
        }

        // Unusable range (0xFEA0 - 0xFEFF)
        if (address < 0xFF00)
        {
            return 0xFF;
        }

        // I/O Registers (0xFF00 - 0xFF7F)
        if (address < 0xFF80)
        {
            return ReadIO((byte)address);
        }

        // HRAM (0xFF80 - 0xFFFE)
        if (address < 0xFFFF)
        {
            return Hram[address - 0xFF80];
        }

        // Interrupt Enable (0xFFFF)
        return _interrupts.IE;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ushort address, byte value)
    {
        switch (address & 0xF000)
        {
            // Cartridge ROM (0x0000 - 0x7FFF)
            case 0x0000 or 0x1000 or 0x2000 or 0x3000 or 0x4000 or 0x5000 or 0x6000 or 0x7000:
                Cartridge.WriteRom(address, value);
                break;

            // VRAM (0x8000 - 0x9FFF)
            case 0x8000 or 0x9000:
                _ppu.Vram[(_ppu.VramBank * 0x2000) + (address & 0x1FFF)] = value;
                break;

            // Cartridge RAM (0xA000 - 0xBFFF)
            case 0xA000 or 0xB000:
                Cartridge.WriteRam(address, value);
                break;

            // WRAM Bank 0 (0xC000 - 0xCFFF)
            case 0xC000:
                Wram[address & 0x0FFF] = value;
                break;

            // WRAM Bank 1-7 (0xD000 - 0xDFFF)
            case 0xD000:
                Wram[(WramBank * 0x1000) + (address & 0x0FFF)] = value;
                break;

            // Echo RAM (0xE000 - 0xFDFF)
            case 0xE000:
                Wram[address & 0x0FFF] = value;
                break;
            case 0xF000:
                if (address < 0xFE00)
                {
                    Wram[(WramBank * 0x1000) + (address & 0x0FFF)] = value;
                }
                else
                {
                    WriteHigh(address, value);
                }
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteHigh(ushort address, byte value)
    {
        // OAM (0xFE00 - 0xFE9F)
        if (address < 0xFEA0)
        {
            _ppu.Oam[address - 0xFE00] = value;
            return;
        }

        // Unusable range (0xFEA0 - 0xFEFF)
        if (address < 0xFF00)
        {
            return;
        }

        // I/O Registers (0xFF00 - 0xFF7F)
        if (address < 0xFF80)
        {
            WriteIO((byte)address, value);
            return;
        }

        // HRAM (0xFF80 - 0xFFFE)
        if (address < 0xFFFF)
        {
            Hram[address - 0xFF80] = value;
            return;
        }

        // Interrupt Enable (0xFFFF)
        _interrupts.IE = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte ReadIO(byte reg)
    {
        return reg switch
        {
            0x00 => _joypad.Read(),
            0x01 => SerialData,
            0x02 => (byte)(SerialControl | 0x7E),
            0x04 => _timer.DIV,
            0x05 => _timer.TIMA,
            0x06 => _timer.TMA,
            0x07 => (byte)(_timer.TAC | 0xF8),
            0x0F => _interrupts.IF,
            >= 0x10 and <= 0x3F => _apu.Read(reg),
            0x40 => _ppu.Lcdc,
            0x41 => (byte)(_ppu.Stat | 0x80),
            0x42 => _ppu.Scy,
            0x43 => _ppu.Scx,
            0x44 => _ppu.Ly,
            0x45 => _ppu.Lyc,
            0x47 => _ppu.Bgp,
            0x48 => _ppu.Obp0,
            0x49 => _ppu.Obp1,
            0x4A => _ppu.Wy,
            0x4B => _ppu.Wx,
            0x4D => (byte)((DoubleSpeed ? 0x80 : 0x00) | (SpeedSwitchArmed ? 0x01 : 0x00) | 0x7E),
            0x4F => (byte)(_ppu.VramBank | 0xFE),
            0x55 => Mode == EmulationMode.Cgb ? (byte)(HdmaActive ? (HdmaBlocksRemaining - 1) : 0xFF) : (byte)0xFF,
            0x68 => (byte)(_ppu.Bcps | 0x40),
            0x69 => _ppu.ReadBcpd(),
            0x6A => (byte)(_ppu.Ocps | 0x40),
            0x6B => _ppu.ReadOcpd(),
            0x70 => (byte)(WramBank | 0xF8),
            _ => 0xFF
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteIO(byte reg, byte value)
    {
        switch (reg)
        {
            case 0x00: _joypad.Write(value); break;
            case 0x01: SerialData = value; break;
            case 0x02:
                SerialControl = value;
                if ((value & 0x81) == 0x81)
                {
                    OnSerialByte?.Invoke(SerialData);
                    SerialControl &= 0x7F; // transfer complete
                    _interrupts.RequestInterrupt(InterruptController.SerialMask);
                }
                break;
            case 0x04: _timer.DIV = value; break;
            case 0x05: _timer.TIMA = value; break;
            case 0x06: _timer.TMA = value; break;
            case 0x07: _timer.TAC = value; break;
            case 0x0F: _interrupts.IF = value; break;
            case >= 0x10 and <= 0x3F: _apu.Write(reg, value); break;
            case 0x40: _ppu.SetLcdc(value); break;
            case 0x41: _ppu.SetStat(value); break;
            case 0x42: _ppu.Scy = value; break;
            case 0x43: _ppu.Scx = value; break;
            case 0x44: /* LY is read only */ break;
            case 0x45: _ppu.Lyc = value; break;
            case 0x46: PerformOamDma(value); break;
            case 0x47: _ppu.Bgp = value; break;
            case 0x48: _ppu.Obp0 = value; break;
            case 0x49: _ppu.Obp1 = value; break;
            case 0x4A: _ppu.Wy = value; break;
            case 0x4B: _ppu.Wx = value; break;
            case 0x4D:
                if (Mode == EmulationMode.Cgb)
                {
                    SpeedSwitchArmed = (value & 0x01) != 0;
                }
                break;
            case 0x4F:
                if (Mode == EmulationMode.Cgb)
                {
                    _ppu.VramBank = value & 0x01;
                }
                break;
            case 0x51: HdmaSource = (ushort)((HdmaSource & 0x00FF) | (value << 8)); break;
            case 0x52: HdmaSource = (ushort)((HdmaSource & 0xFF00) | (value & 0xF0)); break;
            case 0x53: HdmaDest = (ushort)((HdmaDest & 0x00FF) | ((value & 0x1F) << 8)); break;
            case 0x54: HdmaDest = (ushort)((HdmaDest & 0xFF00) | (value & 0xF0)); break;
            case 0x55:
                if (Mode == EmulationMode.Cgb)
                {
                    StartHdma(value);
                }
                break;
            case 0x68: _ppu.Bcps = value; break;
            case 0x69: _ppu.WriteBcpd(value); break;
            case 0x6A: _ppu.Ocps = value; break;
            case 0x6B: _ppu.WriteOcpd(value); break;
            case 0x70:
                if (Mode == EmulationMode.Cgb)
                {
                    int b = value & 0x07;
                    WramBank = (b == 0) ? 1 : b;
                }
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PerformOamDma(byte highByte)
    {
        ushort baseAddr = (ushort)(highByte << 8);
        for (int i = 0; i < 160; i++)
        {
            _ppu.Oam[i] = Read((ushort)(baseAddr + i));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StartHdma(byte value)
    {
        // If HBlank DMA is active and bit 7 is 0, cancel transfer
        if (HdmaActive && (value & 0x80) == 0)
        {
            HdmaActive = false;
            Hdma5 |= 0x80;
            return;
        }

        HdmaBlocksRemaining = (value & 0x7F) + 1;
        bool isHBlankDma = (value & 0x80) != 0;

        if (isHBlankDma)
        {
            HdmaActive = true;
            Hdma5 = (byte)(value & 0x7F);
        }
        else
        {
            // General Purpose DMA: transfer all blocks immediately
            for (int i = 0; i < HdmaBlocksRemaining; i++)
            {
                CopyHdmaBlock();
            }
            HdmaActive = false;
            Hdma5 = 0xFF;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnPpuHBlank()
    {
        if (Mode == EmulationMode.Cgb && HdmaActive)
        {
            CopyHdmaBlock();
            HdmaBlocksRemaining--;

            if (HdmaBlocksRemaining == 0)
            {
                HdmaActive = false;
                Hdma5 = 0xFF;
            }
            else
            {
                Hdma5 = (byte)(HdmaBlocksRemaining - 1);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CopyHdmaBlock()
    {
        ushort src = (ushort)(HdmaSource & 0xFFF0);
        ushort dst = (ushort)(0x8000 | (HdmaDest & 0x1FF0));

        for (int i = 0; i < 16; i++)
        {
            Write((ushort)(dst + i), Read((ushort)(src + i)));
        }

        HdmaSource += 16;
        HdmaDest += 16;
    }
}