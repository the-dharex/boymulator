using System.Diagnostics;
using Boymulator.Audio;
using Boymulator.Core;
using Boymulator.Core.Common;
using Boymulator.Core.Input;
using Boymulator.Graphics.Vulkan;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

/* 
 * This emulator passed 7 out of 8 sections of the Blargg's Game Boy hardware test suite;
 * for the 'oam_bug' section, it only passed '3-non_causes' and '6-timing_no_bug'.
 */

namespace Boymulator;

internal static class Program
{
    private static GameBoy _gameBoy = null!;
    private static VulkanRenderer? _renderer;
    private static OpenALAudioPlayer? _audioPlayer;
    private static IInputContext? _inputContext;
    private static string _currentRomPath = "";
    private static string? _currentSavePath;
    private static int _frameCount;
    private static double _fpsTimer;
    private static double _saveFlushTimer;

    private static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("==================================================");
        Console.WriteLine("      BOYMULATOR - Game Boy & Game Boy Color      ");
        Console.WriteLine("==================================================");

        _gameBoy = new GameBoy();

        if (args.Length > 0 && args[0] == "--benchmark")
        {
            int frames = args.Length > 1 && int.TryParse(args[1], out int f) ? f : 1000;
            RunBenchmark(frames);
            return;
        }

        if (args.Length > 0 && args[0] == "--test")
        {
            string testRom = args.Length > 1 ? args[1] : "";
            int frames = args.Length > 2 && int.TryParse(args[2], out int f) ? f : 300;
            RunTestRom(testRom, frames);
            return;
        }

        string? romPath = args.Length > 0 ? args[0] : null;
        if (!string.IsNullOrEmpty(romPath) && File.Exists(romPath))
        {
            LoadRomFile(romPath);
        }
        else
        {
            Console.WriteLine("[Info] No ROM file passed in arguments. Loading built-in test ROM.");
            Console.WriteLine("[Hint] You can drag-and-drop any .gb or .gbc file into the window at any time!");
            _gameBoy.LoadRom(TestRom.Create());
            _currentRomPath = "Built-in Test Pattern";
        }

        var options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(160 * 4, 144 * 4), // 640x576 (4x scale)
            Title = "Boymulator - Game Boy & Game Boy Color",
            FramesPerSecond = 60.0,
            UpdatesPerSecond = 0.0, // Uncapped update, driven by frame render
            VSync = true
        };

        using var window = Window.Create(options);

        window.Load += () =>
        {
            Console.WriteLine("[Vulkan] Initializing Vulkan renderer via Silk.NET...");
            _renderer = new VulkanRenderer(window);
            Console.WriteLine("[Vulkan] Vulkan renderer initialized successfully.");

            Console.WriteLine("[Audio] Initializing OpenAL audio via Silk.NET...");
            _audioPlayer = new OpenALAudioPlayer();

            _inputContext = window.CreateInput();
            foreach (var keyboard in _inputContext.Keyboards)
            {
                keyboard.KeyDown += OnKeyDown;
                keyboard.KeyUp += OnKeyUp;
            }

            foreach (var gamepad in _inputContext.Gamepads)
            {
                gamepad.ButtonDown += OnGamepadButtonDown;
                gamepad.ButtonUp += OnGamepadButtonUp;
            }

            UpdateWindowTitle(window, 60.0);
        };

        window.FramebufferResize += (newSize) =>
        {
            _renderer?.OnFramebufferResize();
        };

        window.FileDrop += (files) =>
        {
            string? droppedFile = files.FirstOrDefault(f =>
                f.EndsWith(".gb", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".gbc", StringComparison.OrdinalIgnoreCase));

            if (droppedFile != null && File.Exists(droppedFile))
            {
                LoadRomFile(droppedFile);
                UpdateWindowTitle(window, 60.0);
            }
        };

        const double TargetFps = 4194304.0 / 70224.0; // 59.72750057 Hz (Exact Game Boy LCD frequency)
        long ticksPerFrame = (long)(Stopwatch.Frequency / TargetFps);
        long nextFrameTicks = Stopwatch.GetTimestamp();

        var stopwatch = Stopwatch.StartNew();
        double lastTime = stopwatch.Elapsed.TotalSeconds;

        window.Render += (delta) =>
        {
            if (_renderer == null) return;

            // Frame Pacing: Lock emulation to 59.73 Hz regardless of display refresh rate
            long now = Stopwatch.GetTimestamp();
            while (now < nextFrameTicks)
            {
                long diff = nextFrameTicks - now;
                int ms = (int)(diff * 1000 / Stopwatch.Frequency);
                if (ms > 1)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.SpinWait(20);
                }
                now = Stopwatch.GetTimestamp();
            }
            nextFrameTicks += ticksPerFrame;
            if (now - nextFrameTicks > ticksPerFrame * 2)
            {
                nextFrameTicks = now;
            }

            // Advance Game Boy emulation by exactly one full video frame
            var framePixels = _gameBoy.StepFrame();

            _renderer.RenderFrame(framePixels);
            _audioPlayer?.Update(_gameBoy);

            _frameCount++;
            double currentTime = stopwatch.Elapsed.TotalSeconds;
            double elapsed = currentTime - lastTime;

            _fpsTimer += elapsed;
            if (_fpsTimer >= 1.0)
            {
                double fps = _frameCount / _fpsTimer;
                UpdateWindowTitle(window, fps);
                _frameCount = 0;
                _fpsTimer = 0.0;
            }

            // Periodic auto-save flush (every 1 second if dirty)
            _saveFlushTimer += elapsed;
            if (_saveFlushTimer >= 1.0)
            {
                _saveFlushTimer = 0.0;
                if (_gameBoy.IsSaveDirty)
                {
                    FlushSaveData();
                }
            }

            lastTime = currentTime;
        };

        window.Closing += () =>
        {
            FlushSaveData();
            _audioPlayer?.Dispose();
            _inputContext?.Dispose();
            _renderer?.Dispose();
        };

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            FlushSaveData();
        };

        Console.WriteLine("\n[Controls]");
        Console.WriteLine("  D-Pad:  Arrow Keys or W/A/S/D");
        Console.WriteLine("  A:      K or X");
        Console.WriteLine("  B:      J or Z");
        Console.WriteLine("  Start:  Enter");
        Console.WriteLine("  Select: Right Shift / Space / Backspace\n");

        window.Run();
    }

    private static void RunBenchmark(int totalFrames)
    {
        Console.WriteLine($"[Benchmark] Loading built-in test ROM for {totalFrames} frames...");
        _gameBoy.LoadRom(TestRom.Create());

        // Warm up JIT
        for (int i = 0; i < 50; i++)
        {
            _gameBoy.StepFrame();
        }

        Console.WriteLine($"[Benchmark] Executing {totalFrames} frames of full emulation (CPU, PPU, Memory, Timers)...");
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < totalFrames; i++)
        {
            var pixels = _gameBoy.StepFrame();
        }

        sw.Stop();
        double seconds = sw.Elapsed.TotalSeconds;
        double fps = totalFrames / seconds;
        double speedup = fps / 59.73;

        Console.WriteLine($"[Benchmark] Completed {totalFrames} frames in {seconds:F3}s");
        Console.WriteLine($"[Benchmark] Emulation Throughput: {fps:F0} FPS ({speedup:F1}x real-time speed!)");
    }

    private static void LoadRomFile(string path)
    {
        try
        {
            // Flush save data of previous ROM if needed
            FlushSaveData();

            byte[] data = File.ReadAllBytes(path);
            _gameBoy.LoadRom(data);
            _currentRomPath = Path.GetFileName(path);
            _currentSavePath = Path.ChangeExtension(path, ".sav");

            // Automatically load existing .sav file if present next to the ROM
            if (File.Exists(_currentSavePath))
            {
                try
                {
                    byte[] saveData = File.ReadAllBytes(_currentSavePath);
                    _gameBoy.LoadSaveData(saveData);
                    Console.WriteLine($"[Save] Loaded save file: {Path.GetFileName(_currentSavePath)} ({saveData.Length} bytes)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Save] Failed to read save file '{_currentSavePath}': {ex.Message}");
                }
            }

            string hybridTag = _gameBoy.Cartridge?.IsHybrid == true ? " [Hybrid - CGB Color Mode]" : "";
            Console.WriteLine($"[ROM] Loaded: {_gameBoy.Cartridge?.Title} ({_currentRomPath}) | Mode: {_gameBoy.Mode}{hybridTag}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to load ROM '{path}': {ex.Message}");
        }
    }

    private static void FlushSaveData()
    {
        if (string.IsNullOrEmpty(_currentSavePath) || !_gameBoy.IsSaveDirty) return;

        byte[]? saveData = _gameBoy.GetSaveData();
        if (saveData == null || saveData.Length == 0) return;

        try
        {
            // Atomic write to prevent file corruption on power loss or crash
            string tempPath = _currentSavePath + ".tmp";
            File.WriteAllBytes(tempPath, saveData);
            File.Move(tempPath, _currentSavePath, overwrite: true);
            _gameBoy.ClearSaveDirty();
            Console.WriteLine($"[Save] Saved to disk: {Path.GetFileName(_currentSavePath)} ({saveData.Length} bytes)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Save] Failed to save to disk '{_currentSavePath}': {ex.Message}");
        }
    }

    private static void UpdateWindowTitle(IWindow window, double fps)
    {
        string title = _gameBoy.Cartridge?.Title ?? _currentRomPath;
        string mode = _gameBoy.Mode == EmulationMode.Cgb 
            ? (_gameBoy.Cartridge?.IsHybrid == true ? "Game Boy Color (Hybrid)" : "Game Boy Color") 
            : "Game Boy DMG";
        window.Title = $"Boymulator [{mode}] - {title} ({fps:F1} FPS)";
    }

    private static void OnKeyDown(IKeyboard keyboard, Key key, int keyCode) => HandleKey(key, true);
    private static void OnKeyUp(IKeyboard keyboard, Key key, int keyCode) => HandleKey(key, false);

    private static void HandleKey(Key key, bool pressed)
    {
        switch (key)
        {
            case Key.Right or Key.D:
                _gameBoy.SetKeyState(GameBoyKey.Right, pressed);
                break;
            case Key.Left or Key.A:
                _gameBoy.SetKeyState(GameBoyKey.Left, pressed);
                break;
            case Key.Up or Key.W:
                _gameBoy.SetKeyState(GameBoyKey.Up, pressed);
                break;
            case Key.Down or Key.S:
                _gameBoy.SetKeyState(GameBoyKey.Down, pressed);
                break;
            case Key.K or Key.X:
                _gameBoy.SetKeyState(GameBoyKey.A, pressed);
                break;
            case Key.J or Key.Z:
                _gameBoy.SetKeyState(GameBoyKey.B, pressed);
                break;
            case Key.Enter:
                _gameBoy.SetKeyState(GameBoyKey.Start, pressed);
                break;
            case Key.ShiftRight or Key.ShiftLeft or Key.Space or Key.Backspace:
                _gameBoy.SetKeyState(GameBoyKey.Select, pressed);
                break;
        }
    }

    private static void OnGamepadButtonDown(IGamepad gamepad, Button button) => HandleGamepadButton(button.Name, true);
    private static void OnGamepadButtonUp(IGamepad gamepad, Button button) => HandleGamepadButton(button.Name, false);

    private static void HandleGamepadButton(ButtonName button, bool pressed)
    {
        switch (button)
        {
            case ButtonName.DPadRight:
                _gameBoy.SetKeyState(GameBoyKey.Right, pressed);
                break;
            case ButtonName.DPadLeft:
                _gameBoy.SetKeyState(GameBoyKey.Left, pressed);
                break;
            case ButtonName.DPadUp:
                _gameBoy.SetKeyState(GameBoyKey.Up, pressed);
                break;
            case ButtonName.DPadDown:
                _gameBoy.SetKeyState(GameBoyKey.Down, pressed);
                break;
            case ButtonName.A:
                _gameBoy.SetKeyState(GameBoyKey.A, pressed);
                break;
            case ButtonName.B:
                _gameBoy.SetKeyState(GameBoyKey.B, pressed);
                break;
            case ButtonName.Start:
                _gameBoy.SetKeyState(GameBoyKey.Start, pressed);
                break;
            case ButtonName.Back:
                _gameBoy.SetKeyState(GameBoyKey.Select, pressed);
                break;
        }
    }

    private static void RunTestRom(string path, int maxFrames)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[Error] ROM not found: {path}");
            return;
        }

        byte[] romBytes = File.ReadAllBytes(path);
        _gameBoy.LoadRom(romBytes);
        var sb = new System.Text.StringBuilder();
        _gameBoy.Mmu.OnSerialByte = b =>
        {
            char c = (char)b;
            Console.Write(c);
            sb.Append(c);
        };

        for (int i = 0; i < maxFrames; i++)
        {
            _gameBoy.StepFrame();
            string text = sb.ToString();
            if (text.Contains("Passed") || text.Contains("Failed"))
            {
                for (int j = 0; j < 30; j++) _gameBoy.StepFrame();
                break;
            }
        }
        Console.WriteLine();
    }
}
