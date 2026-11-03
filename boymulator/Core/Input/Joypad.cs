using System.Runtime.CompilerServices;
using Boymulator.Core.Cpu;

namespace Boymulator.Core.Input;

public enum GameBoyKey : byte
{
    Right,
    Left,
    Up,
    Down,
    A,
    B,
    Select,
    Start
}

public sealed class Joypad
{
    private readonly InterruptController _interrupts;

    public byte P1 = 0xCF; // Upper 2 bits always 1 (0xC0)

    // Raw button states: true = pressed
    private bool _right, _left, _up, _down;
    private bool _a, _b, _select, _start;

    public Joypad(InterruptController interrupts)
    {
        _interrupts = interrupts;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetKeyState(GameBoyKey key, bool pressed)
    {
        bool wasPressed = key switch
        {
            GameBoyKey.Right => _right,
            GameBoyKey.Left => _left,
            GameBoyKey.Up => _up,
            GameBoyKey.Down => _down,
            GameBoyKey.A => _a,
            GameBoyKey.B => _b,
            GameBoyKey.Select => _select,
            GameBoyKey.Start => _start,
            _ => false
        };

        switch (key)
        {
            case GameBoyKey.Right: _right = pressed; break;
            case GameBoyKey.Left: _left = pressed; break;
            case GameBoyKey.Up: _up = pressed; break;
            case GameBoyKey.Down: _down = pressed; break;
            case GameBoyKey.A: _a = pressed; break;
            case GameBoyKey.B: _b = pressed; break;
            case GameBoyKey.Select: _select = pressed; break;
            case GameBoyKey.Start: _start = pressed; break;
        }

        // Trigger interrupt on 0 -> 1 transition (new press)
        if (!wasPressed && pressed)
        {
            _interrupts.RequestInterrupt(InterruptController.JoypadMask);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Read()
    {
        byte result = (byte)(0xC0 | (P1 & 0x30));

        // P14 low -> select directional buttons
        if ((P1 & 0x10) == 0)
        {
            if (!_right) result |= 0x01;
            if (!_left)  result |= 0x02;
            if (!_up)    result |= 0x04;
            if (!_down)  result |= 0x08;
        }
        // P15 low -> select action buttons
        else if ((P1 & 0x20) == 0)
        {
            if (!_a)      result |= 0x01;
            if (!_b)      result |= 0x02;
            if (!_select) result |= 0x04;
            if (!_start)  result |= 0x08;
        }
        else
        {
            result |= 0x0F;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(byte value)
    {
        P1 = (byte)((P1 & 0xCF) | (value & 0x30));
    }
}

