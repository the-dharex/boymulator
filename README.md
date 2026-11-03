# Boymulator

A high-performance, cycle-accurate Game Boy (DMG) and Game Boy Color (CGB) emulator written in C# (.NET 10), rendered with **Vulkan** and powered by **OpenAL** through [Silk.NET](https://github.com/dotnet/Silk.NET).

---

## Features

- **Full Emulation**: Accurate Z80-like (LR35902) CPU core, memory mapping, timers, and interrupts.
- **Color & Classic Modes**: Full Game Boy Color (CGB) support, Game Boy (DMG), and hybrid cartridge detection.
- **Accurate PPU**: Scanline-accurate rendering pipeline with dynamic Mode 3 timing and sprite stalling (172–289 dots).
- **APU Sound**: 4-channel audio synthesis (Square 1, Square 2, Wave, Noise) streamed in real-time via OpenAL.
- **Cartridge Mappers**:
  - ROM-Only
  - MBC1 (ROM/RAM banking)
  - MBC2 (integrated RAM)
  - MBC3 (Full RTC quartz oscillator, latching, and save persistence)
  - MBC5
- **Battery Saves**: Automatic `.sav` file management with atomic disk writes to prevent save corruption.
- **Drag & Drop**: Drop any `.gb` or `.gbc` file directly into the window to play immediately.
- **Built-in Fallback**: Built-in test screen with custom retro chiptune melody when launched without a cartridge.

---

## Controls

### Keyboard

| Game Boy | Primary Key | Secondary Key |
| :--- | :--- | :--- |
| **D-Pad Up** | `Up Arrow` | `W` |
| **D-Pad Down** | `Down Arrow` | `S` |
| **D-Pad Left** | `Left Arrow` | `A` |
| **D-Pad Right** | `Right Arrow` | `D` |
| **A** | `K` | `X` |
| **B** | `J` | `Z` |
| **Start** | `Enter` | - |
| **Select** | `Shift` (Left/Right) | `Space` / `Backspace` |

### Gamepad

| Game Boy | Gamepad Button |
| :--- | :--- |
| **D-Pad** | D-Pad (Up, Down, Left, Right) |
| **A** | Button A |
| **B** | Button B |
| **Start** | Start |
| **Select** | Back / Select |

---

## Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Vulkan-capable GPU & drivers

### Run Emulator
```bash
# Launch with built-in test pattern
dotnet run --project boymulator -c Release

# Launch directly with a ROM
dotnet run --project boymulator -c Release -- "path/to/game.gb"
```

You can also drag and drop any `.gb` or `.gbc` file directly into the emulator window at any time.

### Benchmark
To run headless performance benchmarking:
```bash
dotnet run --project boymulator -c Release -- --benchmark 1000
```

