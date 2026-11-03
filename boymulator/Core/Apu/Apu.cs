using System.Runtime.CompilerServices;
using Boymulator.Core.Common;

namespace Boymulator.Core.Apu;

public sealed class Apu
{
    public EmulationMode Mode { get; set; } = EmulationMode.Dmg;

    public const int SampleRate = 44100;
    private const int CpuClock = 4194304;

    // Frame Sequencer: 512 Hz (every 8192 T-cycles)
    private int _frameSequencerCycles;
    private int _frameSequencerStep;
    private int _sampleAccumulator;

    // Registers:
    public bool MasterEnable = true; // NR52 bit 7

    // NR50 (Volume) & NR51 (Panning)
    public byte Nr50 = 0x77;
    public byte Nr51 = 0xF3;

    // --- Channel 1: Pulse with Sweep ---
    public bool Ch1Enabled;
    public byte Nr10; // Sweep
    public byte Nr11; // Duty & Length
    public byte Nr12; // Envelope
    public byte Nr13; // Freq Low
    public byte Nr14; // Freq High & Trigger
    private int _ch1Frequency;
    private int _ch1Timer;
    private int _ch1DutyStep;
    private int _ch1LengthCounter;
    private int _ch1Volume;
    private int _ch1EnvelopeTimer;
    private int _ch1SweepTimer;
    private int _ch1ShadowFreq;
    private bool _ch1SweepEnabled;
    private bool _ch1SweepNegateUsed;

    // --- Channel 2: Pulse ---
    public bool Ch2Enabled;
    public byte Nr21;
    public byte Nr22;
    public byte Nr23;
    public byte Nr24;
    private int _ch2Frequency;
    private int _ch2Timer;
    private int _ch2DutyStep;
    private int _ch2LengthCounter;
    private int _ch2Volume;
    private int _ch2EnvelopeTimer;

    // --- Channel 3: Wave ---
    public bool Ch3Enabled;
    public byte Nr30; // DAC Enable
    public byte Nr31; // Length
    public byte Nr32; // Output Level
    public byte Nr33; // Freq Low
    public byte Nr34; // Freq High & Trigger
    public readonly byte[] WaveRam = new byte[16];
    private int _ch3Frequency;
    private int _ch3Timer;
    private int _ch3Position;
    private int _ch3LengthCounter;
    private bool _waveJustRead;

    // --- Channel 4: Noise ---
    public bool Ch4Enabled;
    public byte Nr41; // Length
    public byte Nr42; // Envelope
    public byte Nr43; // Polynomial Counter
    public byte Nr44; // Trigger
    private int _ch4Timer;
    private int _ch4LengthCounter;
    private int _ch4Volume;
    private int _ch4EnvelopeTimer;
    private ushort _ch4Lfsr = 0x7FFF;

    // Duty Waveforms (12.5%, 25%, 50%, 75%)
    private static readonly byte[][] DutyPatterns = [
        [0, 0, 0, 0, 0, 0, 0, 1], // 12.5%
        [1, 0, 0, 0, 0, 0, 0, 1], // 25.0%
        [1, 0, 0, 0, 0, 1, 1, 1], // 50.0%
        [0, 1, 1, 1, 1, 1, 1, 0]  // 75.0%
    ];

    // High-pass filter capacitor state (DC offset removal)
    private float _prevInLeft;
    private float _prevOutLeft;
    private float _prevInRight;
    private float _prevOutRight;

    // Circular Sample Buffer (Stereo 16-bit)
    private const int BufferSize = 32768;
    private readonly short[] _sampleBuffer = new short[BufferSize];
    private int _writeIndex;
    private int _readIndex;
    private readonly object _lock = new();

    public int AvailableSamples
    {
        get
        {
            lock (_lock)
            {
                return (_writeIndex >= _readIndex)
                    ? (_writeIndex - _readIndex)
                    : (BufferSize - _readIndex + _writeIndex);
            }
        }
    }

    public Apu()
    {
        Reset();
    }

    public void Reset()
    {
        PowerDown();
        MasterEnable = true;
        Nr50 = 0x77;
        Nr51 = 0xF3;

        _prevInLeft = 0;
        _prevOutLeft = 0;
        _prevInRight = 0;
        _prevOutRight = 0;

        Array.Clear(WaveRam);
        Array.Clear(_sampleBuffer);
        _writeIndex = 0;
        _readIndex = 0;
        _sampleAccumulator = 0;
    }

    private void PowerDown()
    {
        MasterEnable = false;
        Ch1Enabled = false;
        Ch2Enabled = false;
        Ch3Enabled = false;
        Ch4Enabled = false;

        Nr10 = 0;
        Nr11 = 0;
        Nr12 = 0;
        Nr13 = 0;
        Nr14 = 0;

        Nr21 = 0;
        Nr22 = 0;
        Nr23 = 0;
        Nr24 = 0;

        Nr30 = 0;
        Nr31 = 0;
        Nr32 = 0;
        Nr33 = 0;
        Nr34 = 0;

        Nr41 = 0;
        Nr42 = 0;
        Nr43 = 0;
        Nr44 = 0;

        Nr50 = 0;
        Nr51 = 0;

        _ch1Frequency = 0;
        _ch1Timer = 0;
        _ch1DutyStep = 0;
        _ch1Volume = 0;
        _ch1EnvelopeTimer = 0;
        _ch1SweepTimer = 0;
        _ch1ShadowFreq = 0;
        _ch1SweepEnabled = false;
        _ch1SweepNegateUsed = false;

        _ch2Frequency = 0;
        _ch2Timer = 0;
        _ch2DutyStep = 0;
        _ch2Volume = 0;
        _ch2EnvelopeTimer = 0;

        _ch3Frequency = 0;
        _ch3Timer = 0;
        _ch3Position = 0;
        _waveJustRead = false;

        _ch4Timer = 0;
        _ch4Volume = 0;
        _ch4EnvelopeTimer = 0;
        _ch4Lfsr = 0x7FFF;

        _frameSequencerCycles = 0;
        _frameSequencerStep = 0;

        // NOTE: On DMG, length counters are preserved across power-down.
        // On CGB, length counters are reset to 0!
        if (Mode == EmulationMode.Cgb)
        {
            _ch1LengthCounter = 0;
            _ch2LengthCounter = 0;
            _ch3LengthCounter = 0;
            _ch4LengthCounter = 0;
        }
    }

    private void PowerUp()
    {
        MasterEnable = true;
        _frameSequencerCycles = 0;
        _frameSequencerStep = 0;
        if (Mode == EmulationMode.Cgb)
        {
            _ch1LengthCounter = 0;
            _ch2LengthCounter = 0;
            _ch3LengthCounter = 0;
            _ch4LengthCounter = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick(int cycles)
    {
        if (!MasterEnable) return;

        // Tick Channel Timers
        TickTimers(cycles);

        // Tick Frame Sequencer (512 Hz = every 8192 T-cycles)
        _frameSequencerCycles += cycles;
        while (_frameSequencerCycles >= 8192)
        {
            _frameSequencerCycles -= 8192;
            TickFrameSequencer();
        }

        // Exact Bresenham Sample Generation at 44100 Hz
        _sampleAccumulator += SampleRate * cycles;
        while (_sampleAccumulator >= CpuClock)
        {
            _sampleAccumulator -= CpuClock;
            GenerateSample();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TickTimers(int cycles)
    {
        // Channel 1 Timer
        _ch1Timer -= cycles;
        while (_ch1Timer <= 0)
        {
            _ch1Timer += Math.Max(4, (2048 - _ch1Frequency) * 4);
            _ch1DutyStep = (_ch1DutyStep + 1) & 7;
        }

        // Channel 2 Timer
        _ch2Timer -= cycles;
        while (_ch2Timer <= 0)
        {
            _ch2Timer += Math.Max(4, (2048 - _ch2Frequency) * 4);
            _ch2DutyStep = (_ch2DutyStep + 1) & 7;
        }

        // Channel 3 Timer
        int ch3Period = Math.Max(2, (2048 - _ch3Frequency) * 2);
        _ch3Timer -= cycles;
        _waveJustRead = false;
        while (_ch3Timer <= 0)
        {
            _ch3Timer += ch3Period;
            _ch3Position = (_ch3Position + 1) & 31;
            _waveJustRead = true;
        }
        if (_waveJustRead && _ch3Timer <= ch3Period - 2)
        {
            _waveJustRead = false;
        }

        // Channel 4 Timer
        _ch4Timer -= cycles;
        while (_ch4Timer <= 0)
        {
            int r = Nr43 & 0x07;
            int s = (Nr43 >> 4) & 0x0F;
            int divisor = (r == 0) ? 8 : (r * 16);
            _ch4Timer += Math.Max(4, divisor << s);

            // LFSR Step
            int xor = (_ch4Lfsr & 1) ^ ((_ch4Lfsr >> 1) & 1);
            _ch4Lfsr = (ushort)((_ch4Lfsr >> 1) | (xor << 14));
            if ((Nr43 & 0x08) != 0) // 7-bit width mode
            {
                _ch4Lfsr = (ushort)((_ch4Lfsr & ~0x40) | (xor << 6));
            }
        }
    }

    private void TickFrameSequencer()
    {
        switch (_frameSequencerStep)
        {
            case 0 or 4:
                TickLengths();
                break;
            case 2 or 6:
                TickLengths();
                TickSweep();
                break;
            case 7:
                TickEnvelopes();
                break;
        }

        _frameSequencerStep = (_frameSequencerStep + 1) & 7;
    }

    private void TickLengths()
    {
        if ((Nr14 & 0x40) != 0 && _ch1LengthCounter > 0)
        {
            if (--_ch1LengthCounter == 0) Ch1Enabled = false;
        }
        if ((Nr24 & 0x40) != 0 && _ch2LengthCounter > 0)
        {
            if (--_ch2LengthCounter == 0) Ch2Enabled = false;
        }
        if ((Nr34 & 0x40) != 0 && _ch3LengthCounter > 0)
        {
            if (--_ch3LengthCounter == 0) Ch3Enabled = false;
        }
        if ((Nr44 & 0x40) != 0 && _ch4LengthCounter > 0)
        {
            if (--_ch4LengthCounter == 0) Ch4Enabled = false;
        }
    }

    private void TickEnvelopes()
    {
        // Channel 1
        int period1 = Nr12 & 0x07;
        if (period1 != 0 && --_ch1EnvelopeTimer <= 0)
        {
            _ch1EnvelopeTimer = period1;
            bool up = (Nr12 & 0x08) != 0;
            if (up && _ch1Volume < 15) _ch1Volume++;
            else if (!up && _ch1Volume > 0) _ch1Volume--;
        }

        // Channel 2
        int period2 = Nr22 & 0x07;
        if (period2 != 0 && --_ch2EnvelopeTimer <= 0)
        {
            _ch2EnvelopeTimer = period2;
            bool up = (Nr22 & 0x08) != 0;
            if (up && _ch2Volume < 15) _ch2Volume++;
            else if (!up && _ch2Volume > 0) _ch2Volume--;
        }

        // Channel 4
        int period4 = Nr42 & 0x07;
        if (period4 != 0 && --_ch4EnvelopeTimer <= 0)
        {
            _ch4EnvelopeTimer = period4;
            bool up = (Nr42 & 0x08) != 0;
            if (up && _ch4Volume < 15) _ch4Volume++;
            else if (!up && _ch4Volume > 0) _ch4Volume--;
        }
    }

    private void TickSweep()
    {
        if (!_ch1SweepEnabled) return;

        if (--_ch1SweepTimer <= 0)
        {
            int period = (Nr10 >> 4) & 0x07;
            _ch1SweepTimer = period == 0 ? 8 : period;

            if (period != 0)
            {
                int shift = Nr10 & 0x07;
                int delta = _ch1ShadowFreq >> shift;
                bool negate = (Nr10 & 0x08) != 0;
                if (negate) _ch1SweepNegateUsed = true;
                int newFreq = negate ? (_ch1ShadowFreq - delta) : (_ch1ShadowFreq + delta);

                if (newFreq > 2047)
                {
                    Ch1Enabled = false;
                }
                else if (shift > 0)
                {
                    _ch1ShadowFreq = newFreq;
                    _ch1Frequency = newFreq;
                    Nr13 = (byte)(newFreq & 0xFF);
                    Nr14 = (byte)((Nr14 & ~0x07) | ((newFreq >> 8) & 0x07));

                    // After updating frequency, calculates a second time for overflow check
                    int delta2 = newFreq >> shift;
                    int newFreq2 = negate ? (newFreq - delta2) : (newFreq + delta2);
                    if (newFreq2 > 2047)
                    {
                        Ch1Enabled = false;
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GenerateSample()
    {
        // Channel 1 (Pulse + Sweep)
        float s1 = 0.0f;
        if (Ch1Enabled && (Nr12 & 0xF8) != 0)
        {
            int duty = (Nr11 >> 6) & 0x03;
            int val = DutyPatterns[duty][_ch1DutyStep] * _ch1Volume;
            s1 = val - 7.5f;
        }

        // Channel 2 (Pulse)
        float s2 = 0.0f;
        if (Ch2Enabled && (Nr22 & 0xF8) != 0)
        {
            int duty = (Nr21 >> 6) & 0x03;
            int val = DutyPatterns[duty][_ch2DutyStep] * _ch2Volume;
            s2 = val - 7.5f;
        }

        // Channel 3 (Wave)
        float s3 = 0.0f;
        if (Ch3Enabled && (Nr30 & 0x80) != 0)
        {
            byte byteSample = WaveRam[_ch3Position / 2];
            int rawSample = ((_ch3Position & 1) == 0) ? (byteSample >> 4) : (byteSample & 0x0F);
            int shift = (Nr32 >> 5) & 0x03 switch
            {
                0 => 4, // Mute
                1 => 0, // 100%
                2 => 1, // 50%
                3 => 2, // 25%
                _ => 4
            };
            int sample = rawSample >> shift;
            s3 = sample - 7.5f;
        }

        // Channel 4 (Noise)
        float s4 = 0.0f;
        if (Ch4Enabled && (Nr42 & 0xF8) != 0)
        {
            int val = ((_ch4Lfsr & 1) == 0) ? _ch4Volume : 0;
            s4 = val - 7.5f;
        }

        // Stereo Panning (NR51)
        float left = 0.0f;
        float right = 0.0f;

        if ((Nr51 & 0x01) != 0) right += s1;
        if ((Nr51 & 0x02) != 0) right += s2;
        if ((Nr51 & 0x04) != 0) right += s3;
        if ((Nr51 & 0x08) != 0) right += s4;

        if ((Nr51 & 0x10) != 0) left += s1;
        if ((Nr51 & 0x20) != 0) left += s2;
        if ((Nr51 & 0x40) != 0) left += s3;
        if ((Nr51 & 0x80) != 0) left += s4;

        // Master Volume (NR50: 0-7 -> scale 1 to 8)
        float volLeft = (((Nr50 >> 4) & 0x07) + 1) / 8.0f;
        float volRight = ((Nr50 & 0x07) + 1) / 8.0f;

        float inLeft = (left / 30.0f) * volLeft;
        float inRight = (right / 30.0f) * volRight;

        // High-pass filter capacitor (DC blocker): y[n] = x[n] - x[n-1] + 0.995 * y[n-1]
        float outLeftF = inLeft - _prevInLeft + 0.995f * _prevOutLeft;
        _prevInLeft = inLeft;
        _prevOutLeft = outLeftF;

        float outRightF = inRight - _prevInRight + 0.995f * _prevOutRight;
        _prevInRight = inRight;
        _prevOutRight = outRightF;

        short outLeft = (short)Math.Clamp(outLeftF * 24000.0f, short.MinValue, short.MaxValue);
        short outRight = (short)Math.Clamp(outRightF * 24000.0f, short.MinValue, short.MaxValue);

        lock (_lock)
        {
            int nextWrite = (_writeIndex + 2) % BufferSize;
            if (nextWrite != _readIndex)
            {
                _sampleBuffer[_writeIndex] = outLeft;
                _sampleBuffer[_writeIndex + 1] = outRight;
                _writeIndex = nextWrite;
            }
        }
    }

    public int ReadSamples(Span<short> buffer)
    {
        int count = 0;
        lock (_lock)
        {
            while (count < buffer.Length && _readIndex != _writeIndex)
            {
                buffer[count++] = _sampleBuffer[_readIndex++];
                if (_readIndex >= BufferSize) _readIndex = 0;
            }
        }
        return count;
    }

    // Register Reading & Writing
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Read(byte reg)
    {
        return reg switch
        {
            0x10 => (byte)(Nr10 | 0x80),
            0x11 => (byte)(Nr11 | 0x3F),
            0x12 => Nr12,
            0x13 => 0xFF,
            0x14 => (byte)(Nr14 | 0xBF),

            0x16 => (byte)(Nr21 | 0x3F),
            0x17 => Nr22,
            0x18 => 0xFF,
            0x19 => (byte)(Nr24 | 0xBF),

            0x1A => (byte)(Nr30 | 0x7F),
            0x1B => 0xFF,
            0x1C => (byte)(Nr32 | 0x9F),
            0x1D => 0xFF,
            0x1E => (byte)(Nr34 | 0xBF),

            0x20 => 0xFF,
            0x21 => Nr42,
            0x22 => Nr43,
            0x23 => (byte)(Nr44 | 0xBF),

            0x24 => Nr50,
            0x25 => Nr51,
            0x26 => (byte)((MasterEnable ? 0x80 : 0x00) |
                           (Ch4Enabled ? 0x08 : 0x00) |
                           (Ch3Enabled ? 0x04 : 0x00) |
                           (Ch2Enabled ? 0x02 : 0x00) |
                           (Ch1Enabled ? 0x01 : 0x00) | 0x70),

            >= 0x30 and <= 0x3F => ReadWaveRam(reg),
            _ => 0xFF
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte ReadWaveRam(byte reg)
    {
        if (Ch3Enabled)
        {
            if (Mode == EmulationMode.Dmg)
            {
                return _waveJustRead ? WaveRam[_ch3Position / 2] : (byte)0xFF;
            }
            return WaveRam[_ch3Position / 2];
        }
        return WaveRam[reg - 0x30];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(byte reg, byte val)
    {
        if (!MasterEnable && reg != 0x26 && (reg < 0x30 || reg > 0x3F))
        {
            // On DMG, length counter registers (NR11, NR21, NR31, NR41) can still be written while powered off.
            // On CGB, all writes while powered off are completely ignored.
            if (Mode == EmulationMode.Dmg && reg is 0x11 or 0x16 or 0x1B or 0x20)
            {
                switch (reg)
                {
                    case 0x11:
                        _ch1LengthCounter = 64 - (val & 0x3F);
                        break;
                    case 0x16:
                        _ch2LengthCounter = 64 - (val & 0x3F);
                        break;
                    case 0x1B:
                        _ch3LengthCounter = 256 - val;
                        break;
                    case 0x20:
                        _ch4LengthCounter = 64 - (val & 0x3F);
                        break;
                }
            }
            return;
        }

        switch (reg)
        {
            // Channel 1
            case 0x10:
            {
                bool oldNegate = (Nr10 & 0x08) != 0;
                bool newNegate = (val & 0x08) != 0;
                Nr10 = val;
                if (oldNegate && !newNegate && _ch1SweepNegateUsed)
                {
                    Ch1Enabled = false;
                }
                break;
            }
            case 0x11:
                Nr11 = val;
                _ch1LengthCounter = 64 - (val & 0x3F);
                break;
            case 0x12:
                Nr12 = val;
                if ((val & 0xF8) == 0) Ch1Enabled = false;
                break;
            case 0x13:
                Nr13 = val;
                _ch1Frequency = (_ch1Frequency & 0x0700) | val;
                break;
            case 0x14:
            {
                bool wasLength = (Nr14 & 0x40) != 0;
                bool isLength = (val & 0x40) != 0;
                bool trigger = (val & 0x80) != 0;
                Nr14 = val;
                _ch1Frequency = (_ch1Frequency & 0x00FF) | ((val & 0x07) << 8);

                bool nextNoLen = (_frameSequencerStep & 1) != 0;

                // Enabling length when next step doesn't clock length causes extra clock
                if (!wasLength && isLength && nextNoLen && _ch1LengthCounter > 0)
                {
                    _ch1LengthCounter--;
                    if (_ch1LengthCounter == 0 && !trigger)
                    {
                        Ch1Enabled = false;
                    }
                }

                // Triggering
                if (trigger)
                {
                    bool wasZero = _ch1LengthCounter == 0;
                    TriggerCh1();

                    // If length was 0, trigger unfreezes it to 64; if length is enabled and nextNoLen, it gets clocked to 63
                    if (wasZero && isLength && nextNoLen)
                    {
                        _ch1LengthCounter--;
                    }
                }
                break;
            }

            // Channel 2
            case 0x16:
                Nr21 = val;
                _ch2LengthCounter = 64 - (val & 0x3F);
                break;
            case 0x17:
                Nr22 = val;
                if ((val & 0xF8) == 0) Ch2Enabled = false;
                break;
            case 0x18:
                Nr23 = val;
                _ch2Frequency = (_ch2Frequency & 0x0700) | val;
                break;
            case 0x19:
            {
                bool wasLength = (Nr24 & 0x40) != 0;
                bool isLength = (val & 0x40) != 0;
                bool trigger = (val & 0x80) != 0;
                Nr24 = val;
                _ch2Frequency = (_ch2Frequency & 0x00FF) | ((val & 0x07) << 8);

                bool nextNoLen = (_frameSequencerStep & 1) != 0;

                if (!wasLength && isLength && nextNoLen && _ch2LengthCounter > 0)
                {
                    _ch2LengthCounter--;
                    if (_ch2LengthCounter == 0 && !trigger)
                    {
                        Ch2Enabled = false;
                    }
                }

                if (trigger)
                {
                    bool wasZero = _ch2LengthCounter == 0;
                    TriggerCh2();

                    if (wasZero && isLength && nextNoLen)
                    {
                        _ch2LengthCounter--;
                    }
                }
                break;
            }

            // Channel 3
            case 0x1A:
                Nr30 = val;
                if ((val & 0x80) == 0) Ch3Enabled = false;
                break;
            case 0x1B:
                Nr31 = val;
                _ch3LengthCounter = 256 - val;
                break;
            case 0x1C: Nr32 = val; break;
            case 0x1D:
                Nr33 = val;
                _ch3Frequency = (_ch3Frequency & 0x0700) | val;
                break;
            case 0x1E:
            {
                bool wasLength = (Nr34 & 0x40) != 0;
                bool isLength = (val & 0x40) != 0;
                bool trigger = (val & 0x80) != 0;
                Nr34 = val;
                _ch3Frequency = (_ch3Frequency & 0x00FF) | ((val & 0x07) << 8);

                bool nextNoLen = (_frameSequencerStep & 1) != 0;

                if (!wasLength && isLength && nextNoLen && _ch3LengthCounter > 0)
                {
                    _ch3LengthCounter--;
                    if (_ch3LengthCounter == 0 && !trigger)
                    {
                        Ch3Enabled = false;
                    }
                }

                if (trigger)
                {
                    bool wasZero = _ch3LengthCounter == 0;
                    TriggerCh3();

                    if (wasZero && isLength && nextNoLen)
                    {
                        _ch3LengthCounter--;
                    }
                }
                break;
            }

            // Channel 4
            case 0x20:
                Nr41 = val;
                _ch4LengthCounter = 64 - (val & 0x3F);
                break;
            case 0x21:
                Nr42 = val;
                if ((val & 0xF8) == 0) Ch4Enabled = false;
                break;
            case 0x22: Nr43 = val; break;
            case 0x23:
            {
                bool wasLength = (Nr44 & 0x40) != 0;
                bool isLength = (val & 0x40) != 0;
                bool trigger = (val & 0x80) != 0;
                Nr44 = val;

                bool nextNoLen = (_frameSequencerStep & 1) != 0;

                if (!wasLength && isLength && nextNoLen && _ch4LengthCounter > 0)
                {
                    _ch4LengthCounter--;
                    if (_ch4LengthCounter == 0 && !trigger)
                    {
                        Ch4Enabled = false;
                    }
                }

                if (trigger)
                {
                    bool wasZero = _ch4LengthCounter == 0;
                    TriggerCh4();

                    if (wasZero && isLength && nextNoLen)
                    {
                        _ch4LengthCounter--;
                    }
                }
                break;
            }

            // Control
            case 0x24: Nr50 = val; break;
            case 0x25: Nr51 = val; break;
            case 0x26:
                bool wasEnabled = MasterEnable;
                bool willEnable = (val & 0x80) != 0;
                if (wasEnabled && !willEnable)
                {
                    PowerDown();
                }
                else if (!wasEnabled && willEnable)
                {
                    PowerUp();
                }
                break;

            case >= 0x30 and <= 0x3F:
                if (Ch3Enabled)
                {
                    if (Mode == EmulationMode.Dmg)
                    {
                        if (_waveJustRead)
                        {
                            WaveRam[_ch3Position / 2] = val;
                        }
                        return;
                    }
                    WaveRam[_ch3Position / 2] = val;
                    return;
                }
                WaveRam[reg - 0x30] = val;
                break;
        }
    }

    private void TriggerCh1()
    {
        Ch1Enabled = (Nr12 & 0xF8) != 0;
        if (_ch1LengthCounter == 0) _ch1LengthCounter = 64;
        _ch1Timer = Math.Max(4, (2048 - _ch1Frequency) * 4);
        _ch1Volume = Nr12 >> 4;
        _ch1EnvelopeTimer = Nr12 & 0x07;
        _ch1ShadowFreq = _ch1Frequency;
        int sweepPeriod = (Nr10 >> 4) & 0x07;
        int sweepShift = Nr10 & 0x07;
        _ch1SweepTimer = sweepPeriod == 0 ? 8 : sweepPeriod;
        _ch1SweepEnabled = (sweepPeriod != 0) || (sweepShift != 0);
        _ch1SweepNegateUsed = false;

        if (sweepShift != 0)
        {
            int delta = _ch1ShadowFreq >> sweepShift;
            bool negate = (Nr10 & 0x08) != 0;
            if (negate) _ch1SweepNegateUsed = true;
            int newFreq = negate ? (_ch1ShadowFreq - delta) : (_ch1ShadowFreq + delta);
            if (newFreq > 2047) Ch1Enabled = false;
        }
    }

    private void TriggerCh2()
    {
        Ch2Enabled = (Nr22 & 0xF8) != 0;
        if (_ch2LengthCounter == 0) _ch2LengthCounter = 64;
        _ch2Timer = Math.Max(4, (2048 - _ch2Frequency) * 4);
        _ch2Volume = Nr22 >> 4;
        _ch2EnvelopeTimer = Nr22 & 0x07;
    }

    private void TriggerCh3()
    {
        // DMG bug: wave RAM gets corrupted if the channel is retriggered
        // when the APU is about to read from it (within 1 APU cycle = 2 T-cycles).
        if (Mode == EmulationMode.Dmg && Ch3Enabled && _ch3Timer is 1 or 2)
        {
            int offset = ((_ch3Position + 1) >> 1) & 0x0F;
            if (offset < 4)
            {
                WaveRam[0] = WaveRam[offset];
            }
            else
            {
                int baseIdx = offset & ~3;
                WaveRam[0] = WaveRam[baseIdx];
                WaveRam[1] = WaveRam[baseIdx + 1];
                WaveRam[2] = WaveRam[baseIdx + 2];
                WaveRam[3] = WaveRam[baseIdx + 3];
            }
        }

        Ch3Enabled = (Nr30 & 0x80) != 0;
        if (_ch3LengthCounter == 0) _ch3LengthCounter = 256;
        _ch3Timer = Math.Max(2, (2048 - _ch3Frequency) * 2) + 6;
        _ch3Position = 0;
        _waveJustRead = false;
    }

    private void TriggerCh4()
    {
        Ch4Enabled = (Nr42 & 0xF8) != 0;
        if (_ch4LengthCounter == 0) _ch4LengthCounter = 64;
        _ch4Volume = Nr42 >> 4;
        _ch4EnvelopeTimer = Nr42 & 0x07;
        _ch4Lfsr = 0x7FFF;

        int r = Nr43 & 0x07;
        int s = (Nr43 >> 4) & 0x0F;
        int divisor = (r == 0) ? 8 : (r * 16);
        _ch4Timer = Math.Max(4, divisor << s);
    }
}
