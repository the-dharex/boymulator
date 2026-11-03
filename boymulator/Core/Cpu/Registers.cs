using System.Runtime.CompilerServices;

namespace Boymulator.Core.Cpu;

public struct Registers
{
    public byte A;
    private byte _f;
    public byte B;
    public byte C;
    public byte D;
    public byte E;
    public byte H;
    public byte L;

    public ushort SP;
    public ushort PC;

    public byte F
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _f;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _f = (byte)(value & 0xF0);
    }

    public ushort AF
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (ushort)((A << 8) | _f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            A = (byte)(value >> 8);
            _f = (byte)(value & 0xF0);
        }
    }

    public ushort BC
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (ushort)((B << 8) | C);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            B = (byte)(value >> 8);
            C = (byte)value;
        }
    }

    public ushort DE
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (ushort)((D << 8) | E);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            D = (byte)(value >> 8);
            E = (byte)value;
        }
    }

    public ushort HL
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (ushort)((H << 8) | L);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            H = (byte)(value >> 8);
            L = (byte)value;
        }
    }

    // Flag bit masks
    private const byte FlagMaskZ = 0x80;
    private const byte FlagMaskN = 0x40;
    private const byte FlagMaskH = 0x20;
    private const byte FlagMaskC = 0x10;

    public bool FlagZ
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_f & FlagMaskZ) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _f = (byte)(value ? (_f | FlagMaskZ) : (_f & ~FlagMaskZ));
    }

    public bool FlagN
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_f & FlagMaskN) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _f = (byte)(value ? (_f | FlagMaskN) : (_f & ~FlagMaskN));
    }

    public bool FlagH
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_f & FlagMaskH) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _f = (byte)(value ? (_f | FlagMaskH) : (_f & ~FlagMaskH));
    }

    public bool FlagC
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_f & FlagMaskC) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _f = (byte)(value ? (_f | FlagMaskC) : (_f & ~FlagMaskC));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetFlags(bool z, bool n, bool h, bool c)
    {
        int flags = (z ? FlagMaskZ : 0) |
                    (n ? FlagMaskN : 0) |
                    (h ? FlagMaskH : 0) |
                    (c ? FlagMaskC : 0);
        _f = (byte)flags;
    }
}

