using System.Runtime.CompilerServices;
using Boymulator.Core.Common;
using Boymulator.Core.Cpu;

namespace Boymulator.Core.Ppu;

public sealed class Ppu
{
    private readonly InterruptController _interrupts;
    public EmulationMode Mode { get; set; } = EmulationMode.Dmg;

    // VRAM: 16KB (2 banks of 8KB for CGB)
    public readonly byte[] Vram = new byte[0x4000];
    public int VramBank; // 0 or 1

    // OAM: 160 bytes
    public readonly byte[] Oam = new byte[160];

    // CRAM: Palette RAM for CGB
    public readonly byte[] BgCram = new byte[64];
    public readonly byte[] ObjCram = new byte[64];

    public byte Bcps;
    public byte Ocps;

    // Registers
    public byte Lcdc = 0x91;
    public byte Stat = 0x85;
    public byte Scy;
    public byte Scx;
    public byte Ly;
    public byte Lyc;
    public byte Wy;
    public byte Wx;

    // DMG Palettes
    public byte Bgp = 0xFC;
    public byte Obp0 = 0xFF;
    public byte Obp1 = 0xFF;

    // Framebuffer: 160x144 pixels (32-bit RGBA)
    public readonly uint[] Framebuffer = new uint[160 * 144];
    public bool FrameReady;

    // Internal timing
    private int _dotClock;
    private int _mode3EndDot = 252;
    private int _windowLineCounter;
    private bool _statInterruptSignal;

    // Per-scanline pixel attributes for sprite/BG priority
    private readonly byte[] _scanlineColorIndex = new byte[160];
    private readonly bool[] _scanlineBgPriority = new bool[160];

    // HDMA callback delegate (invoked during HBlank)
    public Action? OnHBlank;

    // Format is R8G8B8A8Unorm in little-endian uint: (0xFF << 24) | (B << 16) | (G << 8) | R
    private static readonly uint[] DmgColors = [
        0xFF8CDEC6, // Shade 0: Lightest / Background (#C6DE8C)
        0xFF63A584, // Shade 1: Light midtone (#84A563)
        0xFF386138, // Shade 2: Dark shadow (#386138)
        0xFF101808  // Shade 3: Darkest / Black (#081810)
    ];

    public Ppu(InterruptController interrupts)
    {
        _interrupts = interrupts;
        Reset();
    }

    public void Reset()
    {
        Array.Clear(Vram);
        Array.Clear(Oam);
        BgCram.AsSpan().Fill(0xFF); // White default (0x7FFF in RGB555)
        ObjCram.AsSpan().Fill(0xFF);
        Array.Clear(Framebuffer);

        VramBank = 0;
        Bcps = 0;
        Ocps = 0;
        Lcdc = 0x91;
        Stat = 0x85;
        Scy = 0;
        Scx = 0;
        Ly = 0;
        Lyc = 0;
        Wy = 0;
        Wx = 0;
        Bgp = 0xFC;
        Obp0 = 0xFF;
        Obp1 = 0xFF;
        _dotClock = 0;
        _mode3EndDot = 252;
        _windowLineCounter = 0;
        _statInterruptSignal = false;
        FrameReady = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick(int cycles)
    {
        // If LCD is disabled, keep LY at 0 and mode 0
        if ((Lcdc & 0x80) == 0)
        {
            return;
        }

        _dotClock += cycles;

        while (_dotClock >= 456)
        {
            _dotClock -= 456;
            Ly++;

            if (Ly == 144)
            {
                // Enter V-Blank
                SetMode(1);
                _interrupts.RequestInterrupt(InterruptController.VBlankMask);
                FrameReady = true;
            }
            else if (Ly > 153)
            {
                // Reset to top of screen
                Ly = 0;
                _windowLineCounter = 0;
                SetMode(2);
            }

            CheckLyc();

            if (Ly < 144)
            {
                SetMode(2);
            }
        }

        if (Ly < 144)
        {
            int mode = Stat & 0x03;
            if (mode == 2 && _dotClock >= 80)
            {
                // Mode 3: Pixel Transfer
                SetMode(3);
                _mode3EndDot = 80 + CalculateMode3Duration();
                RenderScanline();
            }
            else if (mode == 3 && _dotClock >= _mode3EndDot)
            {
                // Mode 0: H-Blank
                SetMode(0);
                OnHBlank?.Invoke();
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CalculateMode3Duration()
    {
        int duration = 172;

        bool bgEnabled = Mode == EmulationMode.Cgb || (Lcdc & 0x01) != 0;
        if (bgEnabled)
        {
            duration += (Scx & 7);

            // Window penalty: if enabled and visible on current scanline
            if ((Lcdc & 0x20) != 0 && Wy <= Ly && Wx <= 166)
            {
                duration += 6;
            }
        }

        // Sprite penalties (stalls)
        if ((Lcdc & 0x02) != 0)
        {
            int spriteHeight = ((Lcdc & 0x04) != 0) ? 16 : 8;
            Span<int> spriteXCoords = stackalloc int[10];
            int spriteCount = 0;

            for (int i = 0; i < 40; i++)
            {
                int oamIndex = i * 4;
                int spriteY = Oam[oamIndex] - 16;
                if (Ly >= spriteY && Ly < spriteY + spriteHeight)
                {
                    spriteXCoords[spriteCount++] = Oam[oamIndex + 1];
                    if (spriteCount == 10) break;
                }
            }

            for (int i = 0; i < spriteCount; i++)
            {
                int x = spriteXCoords[i];
                if (x >= 168) continue; // Completely off-screen right

                duration += 6;

                // Check if an earlier sprite in this scanline had the same X coordinate
                bool duplicate = false;
                for (int j = 0; j < i; j++)
                {
                    if (spriteXCoords[j] == x)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    int adjustedX = x + (Scx & 7);
                    int stall = Math.Max(0, 5 - (adjustedX & 7));
                    duration += stall;
                }
            }
        }

        return Math.Clamp(duration, 172, 289);
    }

    public void SetStat(byte value)
    {
        Stat = (byte)((Stat & 0x07) | (value & 0xF8));
        UpdateStatInterrupt();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetMode(int mode)
    {
        Stat = (byte)((Stat & ~0x03) | (mode & 0x03));
        UpdateStatInterrupt();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckLyc()
    {
        bool lycMatch = Ly == Lyc;
        if (lycMatch)
        {
            Stat |= 0x04;
        }
        else
        {
            Stat = (byte)(Stat & ~0x04);
        }
        UpdateStatInterrupt();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateStatInterrupt()
    {
        if ((Lcdc & 0x80) == 0) return;

        int mode = Stat & 0x03;
        bool signal = false;

        if ((Stat & 0x40) != 0 && (Stat & 0x04) != 0) signal = true; // LYC=LY
        if ((Stat & 0x20) != 0 && mode == 2) signal = true;          // Mode 2
        if ((Stat & 0x10) != 0 && mode == 1) signal = true;          // Mode 1
        if ((Stat & 0x08) != 0 && mode == 0) signal = true;          // Mode 0

        // Interrupt triggers on rising edge
        if (!_statInterruptSignal && signal)
        {
            _interrupts.RequestInterrupt(InterruptController.LcdStatMask);
        }
        _statInterruptSignal = signal;
    }

    public void SetLcdc(byte value)
    {
        bool wasEnabled = (Lcdc & 0x80) != 0;
        bool isEnabled = (value & 0x80) != 0;
        Lcdc = value;

        if (wasEnabled && !isEnabled)
        {
            Ly = 0;
            _dotClock = 0;
            _mode3EndDot = 252;
            Stat = (byte)(Stat & ~0x03);
            Array.Clear(Framebuffer);
        }
        else if (!wasEnabled && isEnabled)
        {
            _dotClock = 0;
            _mode3EndDot = 252;
            Ly = 0;
            CheckLyc();
            SetMode(2);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RenderScanline()
    {
        int lineOffset = Ly * 160;

        // Render Background & Window
        bool bgWindowEnabled = Mode == EmulationMode.Cgb || (Lcdc & 0x01) != 0;

        if (bgWindowEnabled)
        {
            RenderBackground(lineOffset);
            if ((Lcdc & 0x20) != 0) // Window enabled
            {
                RenderWindow(lineOffset);
            }
        }
        else
        {
            // When BG is disabled in DMG, scanline is color 0 (Lightest background)
            uint blankColor = DmgColors[0];
            Array.Fill(Framebuffer, blankColor, lineOffset, 160);
            Array.Fill(_scanlineColorIndex, (byte)0);
            Array.Fill(_scanlineBgPriority, false);
        }

        // Render Sprites (OBJ)
        if ((Lcdc & 0x02) != 0)
        {
            RenderSprites(lineOffset);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RenderBackground(int lineOffset)
    {
        int mapBase = ((Lcdc & 0x08) != 0) ? 0x1C00 : 0x1800; // 0x9C00 or 0x9800 relative to 0x8000
        bool tileSigned = (Lcdc & 0x10) == 0;

        int y = (Ly + Scy) & 0xFF;
        int tileY = y / 8;
        int fineY = y % 8;

        for (int x = 0; x < 160; x++)
        {
            int scrolledX = (x + Scx) & 0xFF;
            int tileX = scrolledX / 8;
            int fineX = scrolledX % 8;

            int mapIndex = mapBase + (tileY * 32) + tileX;
            byte tileId = Vram[mapIndex];

            // In CGB, bank 1 contains tile attributes
            byte attr = (Mode == EmulationMode.Cgb) ? Vram[0x2000 + mapIndex] : (byte)0;
            int tileBank = (attr & 0x08) != 0 ? 0x2000 : 0;
            bool vFlip = (attr & 0x40) != 0;
            bool hFlip = (attr & 0x20) != 0;
            bool bgPriority = (attr & 0x80) != 0;
            int palNum = attr & 0x07;

            int tileFineY = vFlip ? (7 - fineY) : fineY;
            int tileFineX = hFlip ? (7 - fineX) : fineX;

            int tileDataOffset;
            if (tileSigned)
            {
                sbyte signedId = (sbyte)tileId;
                tileDataOffset = tileBank + 0x1000 + (signedId * 16) + (tileFineY * 2);
            }
            else
            {
                tileDataOffset = tileBank + (tileId * 16) + (tileFineY * 2);
            }

            byte low = Vram[tileDataOffset];
            byte high = Vram[tileDataOffset + 1];

            int bit = 7 - tileFineX;
            int colorIndex = (((high >> bit) & 1) << 1) | ((low >> bit) & 1);

            _scanlineColorIndex[x] = (byte)colorIndex;
            _scanlineBgPriority[x] = bgPriority;

            uint color = (Mode == EmulationMode.Cgb)
                ? GetCgbColor(BgCram, palNum, colorIndex)
                : GetDmgColor(Bgp, colorIndex);

            Framebuffer[lineOffset + x] = color;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RenderWindow(int lineOffset)
    {
        if (Ly < Wy) return;

        int windowX = Wx - 7;
        if (windowX >= 160) return;

        int mapBase = ((Lcdc & 0x40) != 0) ? 0x1C00 : 0x1800;
        bool tileSigned = (Lcdc & 0x10) == 0;

        int tileY = _windowLineCounter / 8;
        int fineY = _windowLineCounter % 8;

        int startX = Math.Max(0, windowX);
        bool lineDrawn = false;

        for (int x = startX; x < 160; x++)
        {
            int wxCoord = x - windowX;
            int tileX = wxCoord / 8;
            int fineX = wxCoord % 8;

            int mapIndex = mapBase + (tileY * 32) + tileX;
            byte tileId = Vram[mapIndex];

            byte attr = (Mode == EmulationMode.Cgb) ? Vram[0x2000 + mapIndex] : (byte)0;
            int tileBank = (attr & 0x08) != 0 ? 0x2000 : 0;
            bool vFlip = (attr & 0x40) != 0;
            bool hFlip = (attr & 0x20) != 0;
            bool bgPriority = (attr & 0x80) != 0;
            int palNum = attr & 0x07;

            int tileFineY = vFlip ? (7 - fineY) : fineY;
            int tileFineX = hFlip ? (7 - fineX) : fineX;

            int tileDataOffset;
            if (tileSigned)
            {
                sbyte signedId = (sbyte)tileId;
                tileDataOffset = tileBank + 0x1000 + (signedId * 16) + (tileFineY * 2);
            }
            else
            {
                tileDataOffset = tileBank + (tileId * 16) + (tileFineY * 2);
            }

            byte low = Vram[tileDataOffset];
            byte high = Vram[tileDataOffset + 1];

            int bit = 7 - tileFineX;
            int colorIndex = (((high >> bit) & 1) << 1) | ((low >> bit) & 1);

            _scanlineColorIndex[x] = (byte)colorIndex;
            _scanlineBgPriority[x] = bgPriority;

            uint color = (Mode == EmulationMode.Cgb)
                ? GetCgbColor(BgCram, palNum, colorIndex)
                : GetDmgColor(Bgp, colorIndex);

            Framebuffer[lineOffset + x] = color;
            lineDrawn = true;
        }

        if (lineDrawn)
        {
            _windowLineCounter++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RenderSprites(int lineOffset)
    {
        int spriteHeight = ((Lcdc & 0x04) != 0) ? 16 : 8;

        // Game Boy can render up to 10 sprites per scanline
        Span<int> visibleSprites = stackalloc int[10];
        int count = 0;

        for (int i = 0; i < 40; i++)
        {
            int oamIndex = i * 4;
            int spriteY = Oam[oamIndex] - 16;
            if (Ly >= spriteY && Ly < spriteY + spriteHeight)
            {
                visibleSprites[count++] = i;
                if (count == 10) break;
            }
        }

        // Sprite priority:
        // In DMG: lower X coordinate takes priority. When X matches, lower OAM index wins.
        // In CGB: lower OAM index always wins regardless of X!
        if (Mode == EmulationMode.Dmg && count > 1)
        {
            // Stable sort by X coordinate ascending
            for (int i = 0; i < count - 1; i++)
            {
                for (int j = 0; j < count - i - 1; j++)
                {
                    int oamA = visibleSprites[j] * 4;
                    int oamB = visibleSprites[j + 1] * 4;
                    if (Oam[oamA + 1] > Oam[oamB + 1])
                    {
                        (visibleSprites[j], visibleSprites[j + 1]) = (visibleSprites[j + 1], visibleSprites[j]);
                    }
                }
            }
        }

        // Render from lowest priority to highest so highest overwrites
        for (int i = count - 1; i >= 0; i--)
        {
            int oamIndex = visibleSprites[i] * 4;
            int spriteY = Oam[oamIndex] - 16;
            int spriteX = Oam[oamIndex + 1] - 8;
            byte tileId = Oam[oamIndex + 2];
            byte flags = Oam[oamIndex + 3];

            if (spriteX <= -8 || spriteX >= 160) continue;

            bool objPriority = (flags & 0x80) != 0;
            bool vFlip = (flags & 0x40) != 0;
            bool hFlip = (flags & 0x20) != 0;
            int tileBank = (Mode == EmulationMode.Cgb && (flags & 0x08) != 0) ? 0x2000 : 0;
            int palNum = (Mode == EmulationMode.Cgb) ? (flags & 0x07) : 0;
            byte dmgPalette = (flags & 0x10) != 0 ? Obp1 : Obp0;

            int lineInSprite = Ly - spriteY;
            if (vFlip)
            {
                lineInSprite = spriteHeight - 1 - lineInSprite;
            }

            if (spriteHeight == 16)
            {
                tileId &= 0xFE; // 8x16 mode ignores lowest bit
            }

            int tileDataOffset = tileBank + (tileId * 16) + (lineInSprite * 2);
            byte low = Vram[tileDataOffset];
            byte high = Vram[tileDataOffset + 1];

            for (int px = 0; px < 8; px++)
            {
                int screenX = spriteX + px;
                if ((uint)screenX >= 160) continue;

                int bit = hFlip ? px : (7 - px);
                int colorIndex = (((high >> bit) & 1) << 1) | ((low >> bit) & 1);

                // Color index 0 is always transparent for sprites
                if (colorIndex == 0) continue;

                // Priority Check:
                // CGB LCDC bit 0 master priority: if 0, sprites are always on top!
                bool masterPriority = (Lcdc & 0x01) != 0;
                if (masterPriority && _scanlineColorIndex[screenX] != 0)
                {
                    if (_scanlineBgPriority[screenX] || objPriority)
                    {
                        continue;
                    }
                }

                uint color = (Mode == EmulationMode.Cgb)
                    ? GetCgbColor(ObjCram, palNum, colorIndex)
                    : GetDmgColor(dmgPalette, colorIndex);

                Framebuffer[lineOffset + screenX] = color;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetDmgColor(byte palette, int colorIndex)
    {
        int shade = (palette >> (colorIndex * 2)) & 0x03;
        return DmgColors[shade];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetCgbColor(byte[] cram, int palIndex, int colorIndex)
    {
        int offset = (palIndex * 8) + (colorIndex * 2);
        ushort raw = (ushort)(cram[offset] | (cram[offset + 1] << 8));

        int r = raw & 0x1F;
        int g = (raw >> 5) & 0x1F;
        int b = (raw >> 10) & 0x1F;

        // Vivid GBC LCD color correction formula:
        // Converts 5-bit RGB to 8-bit RGBA with authentic LCD response
        int cr = (r * 26 + g * 4 + b * 2) >> 2;
        int cg = (g * 24 + b * 8) >> 2;
        int cb = (r * 6 + g * 4 + b * 22) >> 2;

        return 0xFF000000u | ((uint)cb << 16) | ((uint)cg << 8) | (uint)cr;
    }

    // Palette CRAM access methods
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadBcpd() => BgCram[Bcps & 0x3F];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBcpd(byte value)
    {
        BgCram[Bcps & 0x3F] = value;
        if ((Bcps & 0x80) != 0) // Auto-increment
        {
            Bcps = (byte)(0x80 | ((Bcps + 1) & 0x3F));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadOcpd() => ObjCram[Ocps & 0x3F];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteOcpd(byte value)
    {
        ObjCram[Ocps & 0x3F] = value;
        if ((Ocps & 0x80) != 0) // Auto-increment
        {
            Ocps = (byte)(0x80 | ((Ocps + 1) & 0x3F));
        }
    }
}