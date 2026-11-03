using System.Runtime.CompilerServices;
using Boymulator.Core.Cpu;

namespace Boymulator.Core.Timer;

public sealed class TimerController
{
    private readonly InterruptController _interrupts;

    public ushort InternalCounter; // 16-bit divider
    public byte TIMA;             // 0xFF05
    public byte TMA;              // 0xFF06
    public byte TAC;              // 0xFF07

    private bool _previousTimerBit;

    public TimerController(InterruptController interrupts)
    {
        _interrupts = interrupts;
    }

    public byte DIV
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)(InternalCounter >> 8);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            InternalCounter = 0;
            CheckFallingEdge();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick(int cycles)
    {
        // Each cycle step
        for (int i = 0; i < cycles; i += 4)
        {
            InternalCounter += 4;
            CheckFallingEdge();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckFallingEdge()
    {
        bool timerEnabled = (TAC & 0x04) != 0;
        int bitPos = (TAC & 0x03) switch
        {
            0 => 9,  // 4096 Hz
            1 => 3,  // 262144 Hz
            2 => 5,  // 65536 Hz
            3 => 7,  // 16384 Hz
            _ => 9
        };

        bool currentBit = timerEnabled && ((InternalCounter & (1 << bitPos)) != 0);

        // Falling edge: 1 -> 0
        if (_previousTimerBit && !currentBit)
        {
            TIMA++;
            if (TIMA == 0)
            {
                TIMA = TMA;
                _interrupts.RequestInterrupt(InterruptController.TimerMask);
            }
        }

        _previousTimerBit = currentBit;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        InternalCounter = 0;
        TIMA = 0;
        TMA = 0;
        TAC = 0;
        _previousTimerBit = false;
    }
}

