namespace Boymulator.Core.Common;

public static class TestRom
{
    private static readonly byte[] NintendoLogo = [
        0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B, 0x03, 0x73, 0x00, 0x83, 0x00, 0x0C, 0x00, 0x0D,
        0x00, 0x08, 0x11, 0x1F, 0x88, 0x89, 0x00, 0x0E, 0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99,
        0xBB, 0xBB, 0x67, 0x63, 0x6E, 0x0E, 0xEC, 0xCC, 0xDD, 0xDC, 0x99, 0x9F, 0xBB, 0xB9, 0x33, 0x3E
    ];

    // 32x32 Pixel Game Boy Cartridge Art (4x4 tiles)
    // '.' = 0: Background
    // '*' = 1: Grip grooves & shading
    // '#' = 2: Cartridge shell
    // '=' = 3: Label border & highlights
    private static readonly string[] CartridgeArt = [
        "................................",
        ".....######################.....",
        "...##########################...",
        "...##**********************##...",
        "...##**********************##...",
        "...##########################...",
        "...##..==================..##...",
        "...##..==================..##...",
        "...##..==..............==..##...",
        "...##..==...########...==..##...",
        "...##..==...##....##...==..##...",
        "...##..==...########...==..##...",
        "...##..==...##....##...==..##...",
        "...##..==...########...==..##...",
        "...##..==..............==..##...",
        "...##..==================..##...",
        "...##..==================..##...",
        "...##########################...",
        "...##......................##...",
        "...##..........##..........##...",
        "...##.........####.........##...",
        "...##........######........##...",
        "...##.......########.......##...",
        "...##.........####.........##...",
        "...##.........####.........##...",
        "...##.........####.........##...",
        "...##......................##...",
        "...##########################...",
        ".....######################.....",
        ".......##################.......",
        "................................",
        "................................"
    ];

    // Standard 8x8 font bitmaps (MSB on left)
    private static readonly Dictionary<char, byte[]> Font = new()
    {
        [' '] = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
        ['!'] = [0x18, 0x18, 0x18, 0x18, 0x00, 0x18, 0x00, 0x00],
        ['-'] = [0x00, 0x00, 0x00, 0x7E, 0x00, 0x00, 0x00, 0x00],
        ['.'] = [0x00, 0x00, 0x00, 0x00, 0x00, 0x18, 0x18, 0x00],
        [':'] = [0x00, 0x18, 0x18, 0x00, 0x18, 0x18, 0x00, 0x00],
        ['0'] = [0x3C, 0x66, 0x6E, 0x76, 0x66, 0x66, 0x3C, 0x00],
        ['1'] = [0x18, 0x38, 0x18, 0x18, 0x18, 0x18, 0x3C, 0x00],
        ['2'] = [0x3C, 0x66, 0x06, 0x0C, 0x18, 0x30, 0x7E, 0x00],
        ['3'] = [0x3C, 0x66, 0x06, 0x1C, 0x06, 0x66, 0x3C, 0x00],
        ['4'] = [0x66, 0x66, 0x66, 0x7E, 0x06, 0x06, 0x06, 0x00],
        ['5'] = [0x7E, 0x60, 0x7C, 0x06, 0x06, 0x66, 0x3C, 0x00],
        ['6'] = [0x3C, 0x60, 0x7C, 0x66, 0x66, 0x66, 0x3C, 0x00],
        ['7'] = [0x7E, 0x06, 0x0C, 0x18, 0x18, 0x18, 0x18, 0x00],
        ['8'] = [0x3C, 0x66, 0x66, 0x3C, 0x66, 0x66, 0x3C, 0x00],
        ['9'] = [0x3C, 0x66, 0x66, 0x3E, 0x06, 0x06, 0x3C, 0x00],
        ['A'] = [0x3C, 0x66, 0x66, 0x7E, 0x66, 0x66, 0x66, 0x00],
        ['B'] = [0x7C, 0x66, 0x66, 0x7C, 0x66, 0x66, 0x7C, 0x00],
        ['C'] = [0x3C, 0x66, 0x60, 0x60, 0x60, 0x66, 0x3C, 0x00],
        ['D'] = [0x78, 0x6C, 0x66, 0x66, 0x66, 0x6C, 0x78, 0x00],
        ['E'] = [0x7E, 0x60, 0x60, 0x7C, 0x60, 0x60, 0x7E, 0x00],
        ['F'] = [0x7E, 0x60, 0x60, 0x7C, 0x60, 0x60, 0x60, 0x00],
        ['G'] = [0x3C, 0x66, 0x60, 0x6E, 0x66, 0x66, 0x3C, 0x00],
        ['H'] = [0x66, 0x66, 0x66, 0x7E, 0x66, 0x66, 0x66, 0x00],
        ['I'] = [0x3C, 0x18, 0x18, 0x18, 0x18, 0x18, 0x3C, 0x00],
        ['J'] = [0x1E, 0x0C, 0x0C, 0x0C, 0x0C, 0x6C, 0x38, 0x00],
        ['K'] = [0x66, 0x6C, 0x78, 0x70, 0x78, 0x6C, 0x66, 0x00],
        ['L'] = [0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x7E, 0x00],
        ['M'] = [0x63, 0x77, 0x7F, 0x6B, 0x63, 0x63, 0x63, 0x00],
        ['N'] = [0x66, 0x76, 0x7E, 0x7E, 0x6E, 0x66, 0x66, 0x00],
        ['O'] = [0x3C, 0x66, 0x66, 0x66, 0x66, 0x66, 0x3C, 0x00],
        ['P'] = [0x7C, 0x66, 0x66, 0x7C, 0x60, 0x60, 0x60, 0x00],
        ['Q'] = [0x3C, 0x66, 0x66, 0x66, 0x6A, 0x6C, 0x36, 0x00],
        ['R'] = [0x7C, 0x66, 0x66, 0x7C, 0x6C, 0x66, 0x66, 0x00],
        ['S'] = [0x3C, 0x66, 0x60, 0x3C, 0x06, 0x66, 0x3C, 0x00],
        ['T'] = [0x7E, 0x18, 0x18, 0x18, 0x18, 0x18, 0x18, 0x00],
        ['U'] = [0x66, 0x66, 0x66, 0x66, 0x66, 0x66, 0x3C, 0x00],
        ['V'] = [0x66, 0x66, 0x66, 0x66, 0x66, 0x3C, 0x18, 0x00],
        ['W'] = [0x63, 0x63, 0x63, 0x6B, 0x7F, 0x77, 0x63, 0x00],
        ['X'] = [0x66, 0x66, 0x3C, 0x18, 0x3C, 0x66, 0x66, 0x00],
        ['Y'] = [0x66, 0x66, 0x66, 0x3C, 0x18, 0x18, 0x18, 0x00],
        ['Z'] = [0x7E, 0x06, 0x0C, 0x18, 0x30, 0x60, 0x7E, 0x00]
    };

    // Melody: (FreqLow, FreqHigh, VolumeEnvelope, DurationFrames)
    // Gentle retro melody in C major
    private static readonly (byte Low, byte High, byte Vol, byte Duration)[] MelodyNotes = [
        // Phrase 1
        (0x39, 0x07, 0x93, 14), // E5
        (0x59, 0x07, 0x93, 14), // G5
        (0x83, 0x07, 0xA4, 24), // C6
        (0x7B, 0x07, 0x83, 14), // B5
        (0x59, 0x07, 0x83, 14), // G5
        (0x6B, 0x07, 0x94, 24), // A5
        (0x59, 0x07, 0x83, 14), // G5
        (0x39, 0x07, 0x83, 14), // E5
        (0x21, 0x07, 0x83, 24), // D5
        (0x06, 0x07, 0xA5, 36), // C5
        (0x00, 0x00, 0x00, 16), // Rest

        // Phrase 2
        (0x39, 0x07, 0x93, 14), // E5
        (0x59, 0x07, 0x93, 14), // G5
        (0x83, 0x07, 0xA4, 24), // C6
        (0x90, 0x07, 0x93, 14), // D6
        (0x83, 0x07, 0x83, 14), // C6
        (0x6B, 0x07, 0x94, 24), // A5
        (0x59, 0x07, 0x83, 14), // G5
        (0x6B, 0x07, 0x83, 14), // A5
        (0x59, 0x07, 0x83, 14), // G5
        (0x39, 0x07, 0x83, 14), // E5
        (0x21, 0x07, 0x83, 16), // D5
        (0x06, 0x07, 0xB6, 44), // C5
        (0x00, 0x00, 0x00, 80)  // Rest
    ];

    private static void EmitJrNz(byte[] rom, ref int pc, int target)
    {
        rom[pc++] = 0x20;
        int nextPc = pc + 1;
        rom[pc++] = (byte)(target - nextPc);
    }

    private static void EmitJrZ(byte[] rom, ref int pc, int target)
    {
        rom[pc++] = 0x28;
        int nextPc = pc + 1;
        rom[pc++] = (byte)(target - nextPc);
    }

    private static void EmitJr(byte[] rom, ref int pc, int target)
    {
        rom[pc++] = 0x18;
        int nextPc = pc + 1;
        rom[pc++] = (byte)(target - nextPc);
    }

    public static byte[] Create()
    {
        byte[] rom = new byte[32 * 1024];

        // 0x0100: NOP; JP 0x0150
        rom[0x0100] = 0x00;
        rom[0x0101] = 0xC3;
        rom[0x0102] = 0x50;
        rom[0x0103] = 0x01;

        // Nintendo logo
        NintendoLogo.CopyTo(rom.AsSpan(0x0104));

        // Title: "BOYMULATOR"
        byte[] title = "BOYMULATOR"u8.ToArray();
        title.CopyTo(rom.AsSpan(0x0134));

        // CGB Flag: 0x80 (CGB compatible)
        rom[0x0143] = 0x80;

        // Cartridge type: ROM ONLY (0x00)
        rom[0x0147] = 0x00;

        // ROM Size: 32KB (0x00)
        rom[0x0148] = 0x00;

        // Header Checksum (0x014D)
        int chk = 0;
        for (int i = 0x0134; i <= 0x014C; i++)
        {
            chk = chk - rom[i] - 1;
        }
        rom[0x014D] = (byte)chk;

        // Addresses for tables in ROM
        const ushort PalAddr = 0x0210;
        const ushort NoteTableAddr = 0x0220;
        const ushort BgMapAddr = 0x0300;
        const ushort TileDataAddr = 0x0700;

        // 1. Build CGB Background Palette 0 (8 bytes) at PalAddr
        // Color 0: Deep navy (0x2483)
        // Color 1: Soft slate cyan (0x5A0A)
        // Color 2: Warm retro amber/gold (0x12DE)
        // Color 3: Crisp pure white (0x7FFF)
        byte[] palData = [
            0x83, 0x24,
            0x0A, 0x5A,
            0xDE, 0x12,
            0xFF, 0x7F
        ];
        palData.CopyTo(rom.AsSpan(PalAddr));

        // 2. Build Note Table at NoteTableAddr
        int notePtr = NoteTableAddr;
        foreach (var note in MelodyNotes)
        {
            rom[notePtr++] = note.Low;
            rom[notePtr++] = note.High;
            rom[notePtr++] = note.Vol;
            rom[notePtr++] = note.Duration;
        }
        // End of table marker
        rom[notePtr++] = 0xFF;
        rom[notePtr++] = 0xFF;
        rom[notePtr++] = 0xFF;
        rom[notePtr++] = 0xFF;

        // 3. Build 32x32 Background Tilemap (1024 bytes) at BgMapAddr
        byte[] bgMap = new byte[1024];
        Array.Fill(bgMap, (byte)0x20); // Fill with space (tile 0x20)

        // Draw centered text and graphics on screen (20 cols x 18 rows)
        DrawText(bgMap, 2, 5, "BOYMULATOR");

        // Cartridge graphic at rows 4..7, cols 8..11 (tiles 0x01 to 0x10)
        byte tileId = 1;
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                bgMap[(4 + r) * 32 + (8 + c)] = tileId++;
            }
        }

        DrawText(bgMap, 9, 5, "INSERTA UN");
        DrawText(bgMap, 11, 6, "CARTUCHO");
        DrawText(bgMap, 14, 2, "- ARRASTRA .GB -");

        bgMap.CopyTo(rom.AsSpan(BgMapAddr));

        // 4. Build Tile Data (2048 bytes = 128 tiles * 16 bytes) at TileDataAddr
        byte[] tileData = new byte[2048];

        // Convert 32x32 Cartridge Art into 16 Game Boy 2BPP tiles (tiles 0x01 to 0x10)
        for (int ty = 0; ty < 4; ty++)
        {
            for (int tx = 0; tx < 4; tx++)
            {
                int tid = 1 + ty * 4 + tx;
                int tileBase = tid * 16;

                for (int y = 0; y < 8; y++)
                {
                    byte plane0 = 0;
                    byte plane1 = 0;
                    int py = ty * 8 + y;

                    for (int x = 0; x < 8; x++)
                    {
                        int px = tx * 8 + x;
                        char ch = CartridgeArt[py][px];
                        int col = ch switch
                        {
                            '*' => 1,
                            '#' => 2,
                            '=' => 3,
                            _ => 0
                        };

                        if ((col & 1) != 0) plane0 |= (byte)(1 << (7 - x));
                        if ((col & 2) != 0) plane1 |= (byte)(1 << (7 - x));
                    }

                    tileData[tileBase + y * 2] = plane0;
                    tileData[tileBase + y * 2 + 1] = plane1;
                }
            }
        }

        // Convert Font bitmaps into Game Boy 2BPP tiles (Color 3)
        foreach (var (ch, rows) in Font)
        {
            int tid = (byte)ch;
            int tileBase = tid * 16;
            for (int y = 0; y < 8; y++)
            {
                byte b = rows[y];
                // Both planes set to 'b' gives Color 3 (pure white / dark)
                tileData[tileBase + y * 2] = b;
                tileData[tileBase + y * 2 + 1] = b;
            }
        }

        tileData.CopyTo(rom.AsSpan(TileDataAddr));

        // 5. SM83 Code at 0x0150
        int pc = 0x0150;

        // DI
        rom[pc++] = 0xF3;

        // LD SP, 0xFFFE
        rom[pc++] = 0x31; rom[pc++] = 0xFE; rom[pc++] = 0xFF;

        // Wait for V-Blank before disabling LCD
        int waitVbInit = pc;
        rom[pc++] = 0xF0; rom[pc++] = 0x44; // LDH A, (0x44)
        rom[pc++] = 0xFE; rom[pc++] = 0x90; // CP 144
        EmitJrNz(rom, ref pc, waitVbInit);

        // Disable LCD: XOR A; LDH (0x40), A
        rom[pc++] = 0xAF;
        rom[pc++] = 0xE0; rom[pc++] = 0x40;

        // Copy Tile Data to 0x8000 (2048 bytes)
        rom[pc++] = 0x21; rom[pc++] = 0x00; rom[pc++] = 0x80; // LD HL, 0x8000
        rom[pc++] = 0x11; rom[pc++] = (byte)(TileDataAddr & 0xFF); rom[pc++] = (byte)(TileDataAddr >> 8); // LD DE, TileDataAddr
        rom[pc++] = 0x01; rom[pc++] = 0x00; rom[pc++] = 0x08; // LD BC, 0x0800
        int copyTiles = pc;
        rom[pc++] = 0x1A; // LD A, (DE)
        rom[pc++] = 0x22; // LD (HL+), A
        rom[pc++] = 0x13; // INC DE
        rom[pc++] = 0x0B; // DEC BC
        rom[pc++] = 0x78; // LD A, B
        rom[pc++] = 0xB1; // OR C
        EmitJrNz(rom, ref pc, copyTiles);

        // Copy BG Map to 0x9800 (1024 bytes)
        rom[pc++] = 0x21; rom[pc++] = 0x00; rom[pc++] = 0x98; // LD HL, 0x9800
        rom[pc++] = 0x11; rom[pc++] = (byte)(BgMapAddr & 0xFF); rom[pc++] = (byte)(BgMapAddr >> 8); // LD DE, BgMapAddr
        rom[pc++] = 0x01; rom[pc++] = 0x00; rom[pc++] = 0x04; // LD BC, 0x0400
        int copyMap = pc;
        rom[pc++] = 0x1A; // LD A, (DE)
        rom[pc++] = 0x22; // LD (HL+), A
        rom[pc++] = 0x13; // INC DE
        rom[pc++] = 0x0B; // DEC BC
        rom[pc++] = 0x78; // LD A, B
        rom[pc++] = 0xB1; // OR C
        EmitJrNz(rom, ref pc, copyMap);

        // Set DMG Palette: BGP = 0xE4 (0xFF47)
        rom[pc++] = 0x3E; rom[pc++] = 0xE4;
        rom[pc++] = 0xE0; rom[pc++] = 0x47;

        // Set CGB BG Palette 0 (0xFF68 / 0xFF69)
        rom[pc++] = 0x3E; rom[pc++] = 0x80; // Auto-increment index 0
        rom[pc++] = 0xE0; rom[pc++] = 0x68;
        rom[pc++] = 0x21; rom[pc++] = (byte)(PalAddr & 0xFF); rom[pc++] = (byte)(PalAddr >> 8); // LD HL, PalAddr
        rom[pc++] = 0x06; rom[pc++] = 0x08; // LD B, 8
        int copyPal = pc;
        rom[pc++] = 0x2A; // LD A, (HL+)
        rom[pc++] = 0xE0; rom[pc++] = 0x69; // LDH (0x69), A
        rom[pc++] = 0x05; // DEC B
        EmitJrNz(rom, ref pc, copyPal);

        // Reset Scroll SCY = 0, SCX = 0
        rom[pc++] = 0xAF;                   // XOR A
        rom[pc++] = 0xE0; rom[pc++] = 0x42; // LDH (0x42), A (SCY)
        rom[pc++] = 0xE0; rom[pc++] = 0x43; // LDH (0x43), A (SCX)

        // Turn on LCD: LCDC = 0x91 (LCD On, Tile Data 0x8000, Map 0x9800, BG On)
        rom[pc++] = 0x3E; rom[pc++] = 0x91;
        rom[pc++] = 0xE0; rom[pc++] = 0x40;

        // Initialize APU Audio
        rom[pc++] = 0x3E; rom[pc++] = 0x80; rom[pc++] = 0xE0; rom[pc++] = 0x26; // NR52 = 0x80 (Audio On)
        rom[pc++] = 0x3E; rom[pc++] = 0x77; rom[pc++] = 0xE0; rom[pc++] = 0x24; // NR50 = 0x77 (Max Volume)
        rom[pc++] = 0x3E; rom[pc++] = 0xFF; rom[pc++] = 0xE0; rom[pc++] = 0x25; // NR51 = 0xFF (Route all channels)
        rom[pc++] = 0xAF;                   rom[pc++] = 0xE0; rom[pc++] = 0x10; // NR10 = 0x00 (Sweep off)
        rom[pc++] = 0x3E; rom[pc++] = 0x80; rom[pc++] = 0xE0; rom[pc++] = 0x11; // NR11 = 0x80 (Duty 50%)

        // Initialize Music state in HRAM:
        // 0xFFA0 = note_index (0)
        // 0xFFA1 = note_timer (1, triggers note 0 on first frame)
        rom[pc++] = 0xAF;                   rom[pc++] = 0xE0; rom[pc++] = 0xA0;
        rom[pc++] = 0x3C;                   rom[pc++] = 0xE0; rom[pc++] = 0xA1;

        // Main Loop
        int mainLoop = pc;
        int waitVb = pc;
        rom[pc++] = 0xF0; rom[pc++] = 0x44; // LDH A, (0x44)
        rom[pc++] = 0xFE; rom[pc++] = 0x90; // CP 144
        EmitJrNz(rom, ref pc, waitVb);

        // Decrement note timer in 0xFFA1
        rom[pc++] = 0xF0; rom[pc++] = 0xA1; // LDH A, (0xA1)
        rom[pc++] = 0x3D;                   // DEC A
        rom[pc++] = 0xE0; rom[pc++] = 0xA1; // LDH (0xA1), A

        int jrSkipPlay = pc;
        EmitJrNz(rom, ref pc, 0); // patch later

        // Timer reached 0! Load next note from table
        rom[pc++] = 0xF0; rom[pc++] = 0xA0; // LDH A, (0xA0)
        rom[pc++] = 0x5F;                   // LD E, A
        rom[pc++] = 0x16; rom[pc++] = 0x00; // LD D, 0
        rom[pc++] = 0x21; rom[pc++] = (byte)(NoteTableAddr & 0xFF); rom[pc++] = (byte)(NoteTableAddr >> 8); // LD HL, NoteTableAddr
        rom[pc++] = 0x19;                   // ADD HL, DE

        rom[pc++] = 0x2A;                   // LD A, (HL+) (FreqLow)
        rom[pc++] = 0xFE; rom[pc++] = 0xFF; // CP 0xFF (End marker)

        int jrPlay = pc;
        EmitJrNz(rom, ref pc, 0); // patch later

        // Loop to start of table
        rom[pc++] = 0xAF;                   // XOR A
        rom[pc++] = 0xE0; rom[pc++] = 0xA0; // LDH (0xA0), A
        rom[pc++] = 0x21; rom[pc++] = (byte)(NoteTableAddr & 0xFF); rom[pc++] = (byte)(NoteTableAddr >> 8);
        rom[pc++] = 0x2A;                   // LD A, (HL+)

        // playNote:
        int playNote = pc;
        rom[jrPlay + 1] = (byte)(playNote - (jrPlay + 2));

        rom[pc++] = 0x4F;                   // LD C, A (FreqLow)
        rom[pc++] = 0x2A;                   // LD A, (HL+)
        rom[pc++] = 0x47;                   // LD B, A (FreqHigh)
        rom[pc++] = 0x2A;                   // LD A, (HL+)
        rom[pc++] = 0x57;                   // LD D, A (Volume/Envelope)
        rom[pc++] = 0x2A;                   // LD A, (HL+)
        rom[pc++] = 0xE0; rom[pc++] = 0xA1; // LDH (0xA1), A (Duration)

        // Advance note_index by 4
        rom[pc++] = 0xF0; rom[pc++] = 0xA0; // LDH A, (0xA0)
        rom[pc++] = 0xC6; rom[pc++] = 0x04; // ADD A, 4
        rom[pc++] = 0xE0; rom[pc++] = 0xA0; // LDH (0xA0), A

        // Trigger note on Channel 1
        rom[pc++] = 0x7A;                   // LD A, D
        rom[pc++] = 0xE0; rom[pc++] = 0x12; // LDH (0x12), A (NR12 = Volume/Envelope)
        rom[pc++] = 0x79;                   // LD A, C
        rom[pc++] = 0xE0; rom[pc++] = 0x13; // LDH (0x13), A (NR13 = FreqLow)
        rom[pc++] = 0x78;                   // LD A, B
        rom[pc++] = 0xF6; rom[pc++] = 0x80; // OR 0x80
        rom[pc++] = 0xE0; rom[pc++] = 0x14; // LDH (0x14), A (NR14 = 0x80 | FreqHigh)

        // waitVbEnd:
        int waitVbEnd = pc;
        rom[jrSkipPlay + 1] = (byte)(waitVbEnd - (jrSkipPlay + 2));

        int waitVbEndLoop = pc;
        rom[pc++] = 0xF0; rom[pc++] = 0x44; // LDH A, (0x44)
        rom[pc++] = 0xFE; rom[pc++] = 0x90; // CP 144
        EmitJrZ(rom, ref pc, waitVbEndLoop);

        EmitJr(rom, ref pc, mainLoop);

        return rom;
    }

    private static void DrawText(byte[] map, int row, int col, string text)
    {
        for (int i = 0; i < text.Length && (col + i) < 32; i++)
        {
            map[row * 32 + col + i] = (byte)text[i];
        }
    }
}
