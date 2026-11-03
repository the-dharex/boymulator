using System.Runtime.CompilerServices;
using Boymulator.Core.Common;
using Boymulator.Core.Cpu;
using Boymulator.Core.Input;
using Boymulator.Core.Memory;
using Boymulator.Core.Timer;

namespace Boymulator.Core;

public sealed class GameBoy
{
    public EmulationMode Mode { get; private set; } = EmulationMode.Dmg;
    public Cartridge? Cartridge => _mmu.Cartridge;
    public Mmu Mmu => _mmu;
    public Cpu.Cpu Cpu => _cpu;
    public Ppu.Ppu Ppu => _ppu;

    private readonly InterruptController _interrupts;
    private readonly TimerController _timer;
    private readonly Ppu.Ppu _ppu;
    private readonly Joypad _joypad;
    private readonly Apu.Apu _apu;
    private readonly Mmu _mmu;
    private readonly Cpu.Cpu _cpu;

    public GameBoy()
    {
        _interrupts = new InterruptController();
        _timer = new TimerController(_interrupts);
        _ppu = new Ppu.Ppu(_interrupts);
        _joypad = new Joypad(_interrupts);
        _apu = new Apu.Apu();
        _mmu = new Mmu(_interrupts, _timer, _ppu, _joypad, _apu);
        _cpu = new Cpu.Cpu(_mmu, _interrupts, _timer, _ppu, _apu);
    }

    public void LoadRom(byte[] romBytes, EmulationMode? forceMode = null)
    {
        var cartridge = Cartridge.Create(romBytes);
        _mmu.Cartridge = cartridge;

        Mode = forceMode ?? (cartridge.IsCgb ? EmulationMode.Cgb : EmulationMode.Dmg);
        _cpu.Mode = Mode;
        _ppu.Mode = Mode;
        _mmu.Mode = Mode;
        _apu.Mode = Mode;

        Reset();
    }

    public void Reset()
    {
        _interrupts.Reset();
        _timer.Reset();
        _ppu.Reset();
        _apu.Reset();
        _apu.Mode = Mode;
        _mmu.Reset();
        _cpu.Reset();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public ReadOnlySpan<uint> StepFrame()
    {
        while (!_ppu.FrameReady)
        {
            _cpu.Step();
        }

        _ppu.FrameReady = false;
        return _ppu.Framebuffer;
    }

    public int AvailableAudioSamples
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _apu.AvailableSamples;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadAudioSamples(Span<short> buffer)
    {
        return _apu.ReadSamples(buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetKeyState(GameBoyKey key, bool pressed)
    {
        _joypad.SetKeyState(key, pressed);
    }

    public byte[]? GetSaveData() => Cartridge?.GetSaveData();
    public void LoadSaveData(ReadOnlySpan<byte> data) => Cartridge?.LoadSaveData(data);
    public bool IsSaveDirty => Cartridge?.IsSaveDirty ?? false;
    public void ClearSaveDirty()
    {
        if (Cartridge != null) Cartridge.IsSaveDirty = false;
    }
}

