using System.Runtime.CompilerServices;
using Silk.NET.OpenAL;

namespace Boymulator.Audio;

public sealed unsafe class OpenALAudioPlayer : IDisposable
{
    private readonly ALContext _alc = ALContext.GetApi();
    private readonly AL _al = AL.GetApi();

    private Device* _device;
    private Context* _context;
    private uint _source;

    private const int BufferCount = 4;
    private readonly uint[] _buffers = new uint[BufferCount];
    private const int FrameBufferSize = 735 * 2; // ~16.6ms of stereo 16-bit samples at 44100 Hz
    private readonly short[] _transferBuffer = new short[FrameBufferSize];

    public bool IsInitialized { get; private set; }

    public OpenALAudioPlayer()
    {
        try
        {
            _device = _alc.OpenDevice(null);
            if (_device == null)
            {
                Console.WriteLine("[Audio] Warning: Could not open default OpenAL audio device.");
                return;
            }

            _context = _alc.CreateContext(_device, null);
            if (_context == null)
            {
                Console.WriteLine("[Audio] Warning: Could not create OpenAL context.");
                return;
            }

            _alc.MakeContextCurrent(_context);

            _source = _al.GenSource();

            fixed (uint* pBuffers = _buffers)
            {
                _al.GenBuffers(BufferCount, pBuffers);
            }

            // Pre-queue silent buffers
            Array.Clear(_transferBuffer);
            fixed (short* pData = _transferBuffer)
            {
                for (int i = 0; i < BufferCount; i++)
                {
                    uint buf = _buffers[i];
                    _al.BufferData(buf, BufferFormat.Stereo16, pData, FrameBufferSize * sizeof(short), 44100);
                    _al.SourceQueueBuffers(_source, 1, &buf);
                }
            }

            _al.SourcePlay(_source);
            IsInitialized = true;
            Console.WriteLine("[Audio] OpenAL audio subsystem initialized successfully (44.1 kHz Stereo).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Audio] Warning: OpenAL initialization failed: {ex.Message}");
            IsInitialized = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(Core.GameBoy gameBoy)
    {
        if (!IsInitialized) return;

        // Check how many buffers have been played and freed by OpenAL
        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);

        if (gameBoy.AvailableAudioSamples > FrameBufferSize * 4)
        {
            // Drop excessive backlog to avoid latency drift
            Span<short> discard = stackalloc short[FrameBufferSize];
            gameBoy.ReadAudioSamples(discard);
        }

        while (processed > 0)
        {
            uint unqueuedBuffer = 0;
            _al.SourceUnqueueBuffers(_source, 1, &unqueuedBuffer);

            // Read samples from Game Boy APU circular buffer
            int samplesRead = gameBoy.ReadAudioSamples(_transferBuffer);

            // If not enough samples (underrun), fill rest with silence
            if (samplesRead < FrameBufferSize)
            {
                Array.Clear(_transferBuffer, samplesRead, FrameBufferSize - samplesRead);
            }

            fixed (short* pData = _transferBuffer)
            {
                _al.BufferData(unqueuedBuffer, BufferFormat.Stereo16, pData, FrameBufferSize * sizeof(short), 44100);
            }

            _al.SourceQueueBuffers(_source, 1, &unqueuedBuffer);
            processed--;
        }

        // Restart source if buffer underrun caused playback to stop
        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
        if ((SourceState)state != SourceState.Playing)
        {
            _al.SourcePlay(_source);
        }
    }

    public void Dispose()
    {
        if (!IsInitialized) return;
        IsInitialized = false;

        try
        {
            _al.SourceStop(_source);

            fixed (uint* pBuffers = _buffers)
            {
                _al.DeleteBuffers(BufferCount, pBuffers);
            }

            _al.DeleteSource(_source);

            if (_context != null)
            {
                _alc.MakeContextCurrent(null);
                _alc.DestroyContext(_context);
                _context = null;
            }

            if (_device != null)
            {
                _alc.CloseDevice(_device);
                _device = null;
            }

            _al.Dispose();
            _alc.Dispose();
        }
        catch
        {
            // Ignore on shutdown
        }
    }
}

