using System.Runtime.CompilerServices;
using System.Text;
using Boymulator.Core.Common;

namespace Boymulator.Core.Memory;

public abstract class Cartridge
{
    public string Title { get; protected set; } = "UNKNOWN";
    public bool IsCgb { get; protected set; }
    public bool IsCgbOnly { get; protected set; }
    public bool IsHybrid => IsCgb && !IsCgbOnly;
    public byte CartridgeType { get; protected set; }
    public int RomSize { get; protected set; }
    public int RamSize { get; protected set; }

    public bool HasBattery => CartridgeType switch
    {
        0x03 or 0x06 or 0x09 or 0x0D or 0x0F or 0x10 or 0x13 or 0x1B or 0x1E or 0x22 or 0xFF => true,
        _ => false
    };

    public virtual bool HasSaveData => HasBattery || RamSize > 0;
    public virtual bool IsSaveDirty { get; set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Tick(int cycles) { }

    public abstract byte ReadRom(ushort address);
    public abstract void WriteRom(ushort address, byte value);
    public abstract byte ReadRam(ushort address);
    public abstract void WriteRam(ushort address, byte value);

    public virtual byte[]? GetSaveData() => null;
    public virtual void LoadSaveData(ReadOnlySpan<byte> data) { }

    public static Cartridge Create(byte[] romData)
    {
        if (romData.Length < 0x150)
        {
            // Fallback for minimal / test data
            var padded = new byte[0x8000];
            romData.CopyTo(padded.AsSpan());
            romData = padded;
        }

        // Title parsing: 0x0134 - 0x0143
        int titleEnd = 0x0143;
        byte cgbFlag = romData[0x0143];
        bool isCgb = (cgbFlag & 0x80) != 0;
        bool isCgbOnly = cgbFlag == 0xC0;

        if (isCgb)
        {
            titleEnd = 0x013E; // CGB title is 11 chars
        }

        int titleLen = 0;
        for (int i = 0x0134; i <= titleEnd && romData[i] != 0; i++)
        {
            titleLen++;
        }

        string title = titleLen > 0 
            ? Encoding.ASCII.GetString(romData.AsSpan(0x0134, titleLen)).Trim() 
            : "UNTITLED";

        byte cartType = romData[0x0147];
        byte romSizeCode = romData[0x0148];
        byte ramSizeCode = romData[0x0149];

        int romSize = 32 * 1024 << romSizeCode;
        int ramSize = ramSizeCode switch
        {
            0x00 => 0,
            0x01 => 2 * 1024,
            0x02 => 8 * 1024,
            0x03 => 32 * 1024,
            0x04 => 128 * 1024,
            0x05 => 64 * 1024,
            _ => 0
        };

        if (cartType == 0x05 || cartType == 0x06)
        {
            ramSize = 512;
        }

        Cartridge cartridge = cartType switch
        {
            0x00 => new Mbc0(romData, ramSize),
            0x01 or 0x02 or 0x03 => new Mbc1(romData, ramSize),
            0x05 or 0x06 => new Mbc2(romData),
            0x0F or 0x10 or 0x11 or 0x12 or 0x13 => new Mbc3(romData, ramSize),
            0x19 or 0x1A or 0x1B or 0x1C or 0x1D or 0x1E => new Mbc5(romData, ramSize),
            _ => new Mbc1(romData, ramSize) // Fallback default
        };

        cartridge.Title = title;
        cartridge.IsCgb = isCgb;
        cartridge.IsCgbOnly = isCgbOnly;
        cartridge.CartridgeType = cartType;
        cartridge.RomSize = romSize;
        cartridge.RamSize = ramSize;

        return cartridge;
    }
}

