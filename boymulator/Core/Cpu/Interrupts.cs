using System.Runtime.CompilerServices;

namespace Boymulator.Core.Cpu;

public sealed class InterruptController
{
    public const byte VBlankMask = 0x01;
    public const byte LcdStatMask = 0x02;
    public const byte TimerMask = 0x04;
    public const byte SerialMask = 0x08;
    public const byte JoypadMask = 0x10;

    public byte IE; // 0xFFFF
    private byte _if; // 0xFF0F

    public bool IME;
    public bool EnableImePending;
    public bool Halted;
    public bool HaltBug;

    public byte IF
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)(_if | 0xE0);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _if = (byte)(value & 0x1F);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RequestInterrupt(byte mask)
    {
        _if |= mask;
        if (Halted)
        {
            Halted = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearInterrupt(byte mask)
    {
        _if = (byte)(_if & ~mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        IE = 0;
        _if = 0;
        IME = false;
        EnableImePending = false;
        Halted = false;
        HaltBug = false;
    }
}

