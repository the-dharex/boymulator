using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Boymulator.Core.Memory;

public sealed class Mbc0 : Cartridge
{
    private readonly byte[] _rom;
    private readonly byte[] _ram;

    public Mbc0(byte[] rom, int ramSize)
    {
        _rom = rom;
        _ram = new byte[Math.Max(ramSize, 0x2000)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte ReadRom(ushort address) => (uint)address < (uint)_rom.Length ? _rom[address] : (byte)0xFF;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteRom(ushort address, byte value) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte ReadRam(ushort address)
    {
        int offset = address - 0xA000;
        return (uint)offset < (uint)_ram.Length ? _ram[offset] : (byte)0xFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteRam(ushort address, byte value)
    {
        int offset = address - 0xA000;
        if ((uint)offset < (uint)_ram.Length)
        {
            if (_ram[offset] != value)
            {
                _ram[offset] = value;
                IsSaveDirty = true;
            }
        }
    }

    public override byte[]? GetSaveData()
    {
        if (RamSize == 0 && !HasBattery) return null;
        int size = RamSize > 0 ? RamSize : _ram.Length;
        byte[] copy = new byte[size];
        _ram.AsSpan(0, Math.Min(size, _ram.Length)).CopyTo(copy);
        return copy;
    }

    public override void LoadSaveData(ReadOnlySpan<byte> data)
    {
        data.Slice(0, Math.Min(data.Length, _ram.Length)).CopyTo(_ram);
    }
}

public sealed class Mbc1 : Cartridge
{
    private readonly byte[] _rom;
    private readonly byte[] _ram;
    private readonly int _romBankMask;
    private readonly int _ramBankMask;

    private bool _ramEnabled;
    private int _romBank = 1;
    private int _ramBank;
    private int _bankingMode; // 0 = ROM banking mode, 1 = RAM banking mode

    public Mbc1(byte[] rom, int ramSize)
    {
        _rom = rom;
        int ramBytes = Math.Max(ramSize, 0x8000);
        _ram = new byte[ramBytes];

        int romBanks = Math.Max(1, rom.Length / 0x4000);
        _romBankMask = romBanks - 1;

        int ramBanks = Math.Max(1, ramBytes / 0x2000);
        _ramBankMask = ramBanks - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte ReadRom(ushort address)
    {
        if (address < 0x4000)
        {
            int bank = (_bankingMode == 1) ? ((_ramBank << 5) & _romBankMask) : 0;
            int offset = (bank * 0x4000) + address;
            return (uint)offset < (uint)_rom.Length ? _rom[offset] : (byte)0xFF;
        }
        else
        {
            int bank = ((_ramBank << 5) | _romBank) & _romBankMask;
            int offset = (bank * 0x4000) + (address - 0x4000);
            return (uint)offset < (uint)_rom.Length ? _rom[offset] : (byte)0xFF;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteRom(ushort address, byte value)
    {
        switch (address & 0xE000)
        {
            case 0x0000: // 0x0000 - 0x1FFF: RAM enable
                _ramEnabled = (value & 0x0F) == 0x0A;
                break;

            case 0x2000: // 0x2000 - 0x3FFF: ROM bank number (lower 5 bits)
                int lower5 = value & 0x1F;
                if (lower5 == 0) lower5 = 1;
                _romBank = lower5;
                break;

            case 0x4000: // 0x4000 - 0x5FFF: RAM bank number or upper 2 bits of ROM bank
                _ramBank = value & 0x03;
                break;

            case 0x6000: // 0x6000 - 0x7FFF: Banking mode select
                _bankingMode = value & 0x01;
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte ReadRam(ushort address)
    {
        if (!_ramEnabled || _ram.Length == 0) return 0xFF;
        int bank = (_bankingMode == 1) ? (_ramBank & _ramBankMask) : 0;
        int offset = (bank * 0x2000) + (address - 0xA000);
        return (uint)offset < (uint)_ram.Length ? _ram[offset] : (byte)0xFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteRam(ushort address, byte value)
    {
        if (!_ramEnabled || _ram.Length == 0) return;
        int bank = (_bankingMode == 1) ? (_ramBank & _ramBankMask) : 0;
        int offset = (bank * 0x2000) + (address - 0xA000);
        if ((uint)offset < (uint)_ram.Length)
        {
            if (_ram[offset] != value)
            {
                _ram[offset] = value;
                IsSaveDirty = true;
            }
        }
    }

    public override byte[]? GetSaveData()
    {
        if (RamSize == 0 && !HasBattery) return null;
        int size = RamSize > 0 ? RamSize : _ram.Length;
        byte[] copy = new byte[size];
        _ram.AsSpan(0, Math.Min(size, _ram.Length)).CopyTo(copy);
        return copy;
    }

    public override void LoadSaveData(ReadOnlySpan<byte> data)
    {
        data.Slice(0, Math.Min(data.Length, _ram.Length)).CopyTo(_ram);
    }
}

public sealed class Mbc2 : Cartridge
{
    private readonly byte[] _rom;
    private readonly byte[] _ram = new byte[512]; // 512 x 4-bit nibbles
    private readonly int _romBankMask;
    private bool _ramEnabled;
    private int _romBank = 1;

    public Mbc2(byte[] rom)
    {
        _rom = rom;
        int romBanks = Math.Max(1, rom.Length / 0x4000);
        _romBankMask = romBanks - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte ReadRom(ushort address)
    {
        if (address < 0x4000)
        {
            return (uint)address < (uint)_rom.Length ? _rom[address] : (byte)0xFF;
        }
        int offset = (_romBank * 0x4000) + (address - 0x4000);
        return (uint)offset < (uint)_rom.Length ? _rom[offset] : (byte)0xFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteRom(ushort address, byte value)
    {
        if (address < 0x4000)
        {
            if ((address & 0x0100) == 0)
            {
                // Bit 8 is 0: RAM enable
                _ramEnabled = (value & 0x0F) == 0x0A;
            }
            else
            {
                // Bit 8 is 1: ROM bank
                int bank = value & 0x0F;
                _romBank = (bank == 0 ? 1 : bank) & _romBankMask;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte ReadRam(ushort address)
    {
        if (!_ramEnabled) return 0xFF;
        int offset = (address - 0xA000) & 0x01FF;
        return (byte)(_ram[offset] | 0xF0); // Upper 4 bits undefined (1s)
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteRam(ushort address, byte value)
    {
        if (!_ramEnabled) return;
        int offset = (address - 0xA000) & 0x01FF;
        byte nibble = (byte)(value & 0x0F);
        if (_ram[offset] != nibble)
        {
            _ram[offset] = nibble;
            IsSaveDirty = true;
        }
    }

    public override byte[]? GetSaveData()
    {
        byte[] copy = new byte[512];
        _ram.AsSpan(0, 512).CopyTo(copy);
        return copy;
    }

    public override void LoadSaveData(ReadOnlySpan<byte> data)
    {
        data.Slice(0, Math.Min(data.Length, 512)).CopyTo(_ram);
    }
}

public sealed class Mbc3 : Cartridge
{
    private readonly byte[] _rom;
    private readonly byte[] _ram;
    private readonly int _romBankMask;
    private readonly int _ramBankMask;

    private bool _ramEnabled;
    private int _romBank = 1;
    private int _ramBank;

    // Live RTC registers
    private byte _rtcS;   // 0x08: Seconds (0-59)
    private byte _rtcM;   // 0x09: Minutes (0-59)
    private byte _rtcH;   // 0x0A: Hours (0-23)
    private byte _rtcDl;  // 0x0B: Lower 8 bits of Day counter (0-255)
    private byte _rtcDh;  // 0x0C: Upper Day bit, Halt flag, Carry bit

    // Latched RTC registers (shadow registers read by CPU when latched)
    private byte _latchedS;
    private byte _latchedM;
    private byte _latchedH;
    private byte _latchedDl;
    private byte _latchedDh;

    // Sub-second precision tracking:
    // Game Boy base clock: 4,194,304 Hz
    // RTC quartz crystal: 32,768 Hz (4,194,304 / 32,768 = 128 cycles per RTC tick)
    private int _rtcCycleAccumulator; // 0..127
    private int _rtcSubSeconds;       // 0..32767

    // Latch control (0x00 -> 0x01 sequence detection)
    private int _rtcLatchState = -1;

    public Mbc3(byte[] rom, int ramSize)
    {
        _rom = rom;
        int ramBytes = Math.Max(ramSize, 0x8000);
        _ram = new byte[ramBytes];

        int romBanks = Math.Max(1, rom.Length / 0x4000);
        _romBankMask = romBanks - 1;

        int ramBanks = Math.Max(1, ramBytes / 0x2000);
        _ramBankMask = ramBanks - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Tick(int cycles)
    {
        // When Halt flag (bit 6 of DH) is set, RTC oscillator is stopped
        if ((_rtcDh & 0x40) != 0) return;

        _rtcCycleAccumulator += cycles;
        if (_rtcCycleAccumulator >= 128)
        {
            int ticks = _rtcCycleAccumulator / 128;
            _rtcCycleAccumulator %= 128;
            _rtcSubSeconds += ticks;
            while (_rtcSubSeconds >= 32768)
            {
                _rtcSubSeconds -= 32768;
                TickSecond();
            }
        }
    }

    private void TickSecond()
    {
        _rtcS++;
        if (_rtcS >= 60)
        {
            _rtcS = 0;
            _rtcM++;
            if (_rtcM >= 60)
            {
                _rtcM = 0;
                _rtcH++;
                if (_rtcH >= 24)
                {
                    _rtcH = 0;
                    int day = _rtcDl | ((_rtcDh & 1) << 8);
                    day++;
                    if (day > 511)
                    {
                        day = 0;
                        _rtcDh |= 0x80; // Set Day Counter Carry bit
                    }
                    _rtcDl = (byte)(day & 0xFF);
                    _rtcDh = (byte)((_rtcDh & ~1) | ((day >> 8) & 1));
                }
            }
        }
    }

    private void AdvanceSeconds(long totalSeconds)
    {
        if (totalSeconds <= 0) return;
        if ((_rtcDh & 0x40) != 0) return; // If halted, time does not advance

        long s = _rtcS + totalSeconds;
        _rtcS = (byte)(s % 60);
        long m = _rtcM + (s / 60);
        _rtcM = (byte)(m % 60);
        long h = _rtcH + (m / 60);
        _rtcH = (byte)(h % 24);
        long daysToAdd = h / 24;

        int currentDays = _rtcDl | ((_rtcDh & 1) << 8);
        long newDays = currentDays + daysToAdd;
        if (newDays >= 512)
        {
            _rtcDh |= 0x80; // Carry bit
        }
        int wrappedDays = (int)(newDays % 512);
        _rtcDl = (byte)(wrappedDays & 0xFF);
        _rtcDh = (byte)((_rtcDh & ~1) | ((wrappedDays >> 8) & 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte ReadRom(ushort address)
    {
        if (address < 0x4000)
        {
            return (uint)address < (uint)_rom.Length ? _rom[address] : (byte)0xFF;
        }
        int offset = (_romBank * 0x4000) + (address - 0x4000);
        return (uint)offset < (uint)_rom.Length ? _rom[offset] : (byte)0xFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteRom(ushort address, byte value)
    {
        switch (address & 0xE000)
        {
            case 0x0000: // RAM / RTC Enable (0x0000-0x1FFF)
                _ramEnabled = (value & 0x0F) == 0x0A;
                break;

            case 0x2000: // ROM Bank Number (7-bit, 0x2000-0x3FFF)
                int bank = value & 0x7F;
                _romBank = (bank == 0 ? 1 : bank) & _romBankMask;
                break;

            case 0x4000: // RAM Bank or RTC Register Select (0x4000-0x5FFF)
                _ramBank = value;
                break;

            case 0x6000: // Latch Clock Data (0x6000-0x7FFF)
                if (_rtcLatchState == 0 && value == 1)
                {
                    _latchedS = _rtcS;
                    _latchedM = _rtcM;
                    _latchedH = _rtcH;
                    _latchedDl = _rtcDl;
                    _latchedDh = _rtcDh;
                }
                _rtcLatchState = value;
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte ReadRam(ushort address)
    {
        if (!_ramEnabled) return 0xFF;
        if (_ramBank <= 0x07) // RAM Bank 0-3 (or 0-7 on MBC30)
        {
            int bank = _ramBank & _ramBankMask;
            int offset = (bank * 0x2000) + (address - 0xA000);
            return (uint)offset < (uint)_ram.Length ? _ram[offset] : (byte)0xFF;
        }
        return _ramBank switch
        {
            0x08 => _latchedS,
            0x09 => _latchedM,
            0x0A => _latchedH,
            0x0B => _latchedDl,
            0x0C => _latchedDh,
            _ => 0xFF
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteRam(ushort address, byte value)
    {
        if (!_ramEnabled) return;
        if (_ramBank <= 0x07) // RAM Bank 0-3 (or 0-7 on MBC30)
        {
            int bank = _ramBank & _ramBankMask;
            int offset = (bank * 0x2000) + (address - 0xA000);
            if ((uint)offset < (uint)_ram.Length)
            {
                if (_ram[offset] != value)
                {
                    _ram[offset] = value;
                    IsSaveDirty = true;
                }
            }
            return;
        }

        switch (_ramBank)
        {
            case 0x08: // RTC S
                _rtcS = (byte)(value & 0x3F);
                _rtcSubSeconds = 0;
                _rtcCycleAccumulator = 0;
                IsSaveDirty = true;
                break;

            case 0x09: // RTC M
                _rtcM = (byte)(value & 0x3F);
                IsSaveDirty = true;
                break;

            case 0x0A: // RTC H
                _rtcH = (byte)(value & 0x1F);
                IsSaveDirty = true;
                break;

            case 0x0B: // RTC DL
                _rtcDl = value;
                IsSaveDirty = true;
                break;

            case 0x0C: // RTC DH
                _rtcDh = (byte)(value & 0xC1);
                IsSaveDirty = true;
                break;
        }
    }

    public override byte[]? GetSaveData()
    {
        if (RamSize == 0 && !HasBattery) return null;
        int ramBytes = RamSize > 0 ? RamSize : _ram.Length;

        // 48-byte BGB / VBA-M standard RTC footer appended to RAM
        byte[] data = new byte[ramBytes + 48];
        _ram.AsSpan(0, Math.Min(ramBytes, _ram.Length)).CopyTo(data);

        int rtcOffset = ramBytes;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rtcOffset + 0, 4), _rtcS);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rtcOffset + 4, 4), _rtcM);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rtcOffset + 8, 4), _rtcH);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rtcOffset + 12, 4), _rtcDl);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rtcOffset + 16, 4), _rtcDh);

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rtcOffset + 20, 4), _latchedS);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rtcOffset + 24, 4), _latchedM);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rtcOffset + 28, 4), _latchedH);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rtcOffset + 32, 4), _latchedDl);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rtcOffset + 36, 4), _latchedDh);

        ulong timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(rtcOffset + 40, 8), timestamp);

        return data;
    }

    public override void LoadSaveData(ReadOnlySpan<byte> data)
    {
        int ramBytes = Math.Min(data.Length, _ram.Length);
        data.Slice(0, ramBytes).CopyTo(_ram);

        if (data.Length >= _ram.Length + 44)
        {
            var rtcData = data.Slice(_ram.Length);
            _rtcS = (byte)BinaryPrimitives.ReadUInt32LittleEndian(rtcData.Slice(0, 4));
            _rtcM = (byte)BinaryPrimitives.ReadUInt32LittleEndian(rtcData.Slice(4, 4));
            _rtcH = (byte)BinaryPrimitives.ReadUInt32LittleEndian(rtcData.Slice(8, 4));
            _rtcDl = (byte)BinaryPrimitives.ReadUInt32LittleEndian(rtcData.Slice(12, 4));
            _rtcDh = (byte)BinaryPrimitives.ReadUInt32LittleEndian(rtcData.Slice(16, 4));

            _latchedS = (byte)BinaryPrimitives.ReadUInt32LittleEndian(rtcData.Slice(20, 4));
            _latchedM = (byte)BinaryPrimitives.ReadUInt32LittleEndian(rtcData.Slice(24, 4));
            _latchedH = (byte)BinaryPrimitives.ReadUInt32LittleEndian(rtcData.Slice(28, 4));
            _latchedDl = (byte)BinaryPrimitives.ReadUInt32LittleEndian(rtcData.Slice(32, 4));
            _latchedDh = (byte)BinaryPrimitives.ReadUInt32LittleEndian(rtcData.Slice(36, 4));

            ulong savedTimestamp;
            if (rtcData.Length >= 48)
            {
                savedTimestamp = BinaryPrimitives.ReadUInt64LittleEndian(rtcData.Slice(40, 8));
            }
            else
            {
                savedTimestamp = BinaryPrimitives.ReadUInt32LittleEndian(rtcData.Slice(40, 4));
            }

            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (savedTimestamp > 0 && savedTimestamp < now && savedTimestamp != 0x7FFF_FFFF_7FFF_FFFF && savedTimestamp != 0x7FFF_FFFF)
            {
                ulong elapsed = now - savedTimestamp;
                AdvanceSeconds((long)elapsed);
                _latchedS = _rtcS;
                _latchedM = _rtcM;
                _latchedH = _rtcH;
                _latchedDl = _rtcDl;
                _latchedDh = _rtcDh;
            }
        }
        else
        {
            _latchedS = _rtcS;
            _latchedM = _rtcM;
            _latchedH = _rtcH;
            _latchedDl = _rtcDl;
            _latchedDh = _rtcDh;
        }
    }
}

public sealed class Mbc5 : Cartridge
{
    private readonly byte[] _rom;
    private readonly byte[] _ram;
    private readonly int _romBankMask;
    private readonly int _ramBankMask;

    private bool _ramEnabled;
    private int _romBank = 1;
    private int _ramBank;

    public Mbc5(byte[] rom, int ramSize)
    {
        _rom = rom;
        int ramBytes = Math.Max(ramSize, 0x20000); // Up to 128KB RAM
        _ram = new byte[ramBytes];

        int romBanks = Math.Max(1, rom.Length / 0x4000);
        _romBankMask = romBanks - 1;

        int ramBanks = Math.Max(1, ramBytes / 0x2000);
        _ramBankMask = ramBanks - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte ReadRom(ushort address)
    {
        if (address < 0x4000)
        {
            return (uint)address < (uint)_rom.Length ? _rom[address] : (byte)0xFF;
        }
        int offset = (_romBank * 0x4000) + (address - 0x4000);
        return (uint)offset < (uint)_rom.Length ? _rom[offset] : (byte)0xFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteRom(ushort address, byte value)
    {
        switch (address & 0xF000)
        {
            case 0x0000 or 0x1000: // RAM enable
                _ramEnabled = (value & 0x0F) == 0x0A;
                break;

            case 0x2000: // Lower 8 bits of ROM bank
                _romBank = ((_romBank & 0x0100) | value) & _romBankMask;
                break;

            case 0x3000: // 9th bit of ROM bank
                _romBank = ((_romBank & 0x00FF) | ((value & 0x01) << 8)) & _romBankMask;
                break;

            case 0x4000 or 0x5000: // RAM bank number (4-bit)
                _ramBank = value & 0x0F;
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte ReadRam(ushort address)
    {
        if (!_ramEnabled || _ram.Length == 0) return 0xFF;
        int bank = _ramBank & _ramBankMask;
        int offset = (bank * 0x2000) + (address - 0xA000);
        return (uint)offset < (uint)_ram.Length ? _ram[offset] : (byte)0xFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteRam(ushort address, byte value)
    {
        if (!_ramEnabled || _ram.Length == 0) return;
        int bank = _ramBank & _ramBankMask;
        int offset = (bank * 0x2000) + (address - 0xA000);
        if ((uint)offset < (uint)_ram.Length)
        {
            if (_ram[offset] != value)
            {
                _ram[offset] = value;
                IsSaveDirty = true;
            }
        }
    }

    public override byte[]? GetSaveData()
    {
        if (RamSize == 0 && !HasBattery) return null;
        int size = RamSize > 0 ? RamSize : _ram.Length;
        byte[] copy = new byte[size];
        _ram.AsSpan(0, Math.Min(size, _ram.Length)).CopyTo(copy);
        return copy;
    }

    public override void LoadSaveData(ReadOnlySpan<byte> data)
    {
        data.Slice(0, Math.Min(data.Length, _ram.Length)).CopyTo(_ram);
    }
}

