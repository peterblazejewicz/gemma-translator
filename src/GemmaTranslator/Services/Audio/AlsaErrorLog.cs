// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services.Audio;

/// <summary>
/// Sends the messages of libasound to <see cref="ILogger"/> at the debug
/// level, in place of the standard error stream of the process. The handler
/// keeps one message and <see cref="Drain"/> writes it.
/// </summary>
/// <remarks>
/// <para>
/// The presence loop reads the list of the devices every 2 s. Each read makes
/// ALSA open "default", which goes through dmix and asym, and Pi OS Lite gives
/// those two no slave because it has no PipeWire and no PulseAudio. libasound
/// then writes 4 lines to the standard error stream, systemd keeps them, and
/// the journal takes about 170 000 lines each day. The messages say nothing
/// about the speakerphone, which the software opens by name and not through
/// "default".
/// </para>
/// <para>
/// CAUTION: snd_lib_error_set_handler takes one handler for the whole process,
/// thus libasound calls this class from EACH of its own threads and not from
/// the presence loop alone. miniaudio calls libasound on its audio thread, and
/// an xrun or a speakerphone that goes away in the middle of a sentence makes
/// libasound write from that thread. The remark of SoundFlowAudioDevice
/// records that a lock on the audio thread gave a deadlock, and AddConsole
/// takes a lock and waits when its queue of 1024 is full. Thus the handler
/// must not call ILogger, must make no memory, and must take no lock. It puts
/// the message in the fields below and the presence loop writes it.
/// </para>
/// </remarks>
internal static partial class AlsaErrorLog
{
    private const string LibraryName = "libasound.so.2";

    private const int Empty = 0;
    private const int Filling = 1;
    private const int Ready = 2;

    // CAUTION: libasound keeps this pointer for the life of the process. With
    // no reference here the collector takes the delegate, and the next message
    // of ALSA calls memory that the process does not hold. This field is that
    // reference, and it is the one cause for a static.
    private static ErrorHandler? _handler;

    // One message and a count of the messages that came while it waited. The
    // 4 lines repeat for the life of the process, thus the second one of a
    // tick says what the first one says. A ring would keep more copies of the
    // same text and it would need more code on the audio thread.
    private static readonly byte[] PendingFile = new byte[128];
    private static readonly byte[] PendingFunction = new byte[128];
    private static readonly byte[] PendingText = new byte[256];

    private static int _state;
    private static int _pendingFileLength;
    private static int _pendingFunctionLength;
    private static int _pendingTextLength;
    private static int _pendingLine;
    private static int _pendingError;
    private static int _dropped;

    // CAUTION: the native type is variadic, and this declaration gives the 5
    // named parameters only:
    //
    //   void (*)(const char *file, int line, const char *function,
    //            int err, const char *fmt, ...)
    //
    // This is correct and it looks incorrect. On arm64 and on x86-64 the named
    // parameters of a variadic call go in the same registers as those of a
    // call that is not variadic, thus a callee that reads 5 and no more reads
    // the correct 5. It must not read the arguments after them.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ErrorHandler(
        IntPtr file,
        int line,
        IntPtr function,
        int error,
        IntPtr format);

    [LibraryImport(LibraryName, EntryPoint = "snd_lib_error_set_handler")]
    private static partial int SetHandler(IntPtr handler);

    /// <summary>Puts the handler in place one time. It does nothing off Linux.</summary>
    public static void Install(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ErrorHandler handler = (file, line, function, error, format) =>
            Write(logger, file, line, function, error, format);

        if (Interlocked.CompareExchange(ref _handler, handler, null) is not null)
        {
            return;
        }

        int result = SetHandler(Marshal.GetFunctionPointerForDelegate(handler));

        if (result != 0)
        {
            LogHandlerNotSet(logger, result);
        }
    }

    /// <summary>
    /// Writes the message that the handler kept. The caller must be a thread
    /// that can wait: this is the call that takes the lock of the log.
    /// </summary>
    public static void Drain(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (Volatile.Read(ref _state) == Ready)
        {
            // The handler cannot write now: it claims the slot from Empty and
            // this thread puts it back to Empty at the end. The slot goes back
            // also when the level went off between the two, or the handler
            // finds it full for the life of the process.
            if (logger.IsEnabled(LogLevel.Debug))
            {
#pragma warning disable CA1873 // The line above is the test that this asks for.
                LogAlsa(
                    logger,
                    Encoding.UTF8.GetString(PendingFile, 0, _pendingFileLength),
                    _pendingLine,
                    Encoding.UTF8.GetString(PendingFunction, 0, _pendingFunctionLength),
                    _pendingError,
                    Encoding.UTF8.GetString(PendingText, 0, _pendingTextLength));
#pragma warning restore CA1873
            }

            Volatile.Write(ref _state, Empty);
        }

        int dropped = Interlocked.Exchange(ref _dropped, 0);

        if (dropped > 0)
        {
            LogDropped(logger, dropped);
        }
    }

    private static void Write(
        ILogger logger, IntPtr file, int line, IntPtr function, int error, IntPtr format)
    {
        // CAUTION: native code calls this method. An exception that leaves it
        // goes into libasound, which cannot take one, and the process stops.
        try
        {
            // With the debug level off the handler does this test and no more,
            // which is the condition that the appliance operates in.
            if (!logger.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _state, Filling, Empty) != Empty)
            {
                Interlocked.Increment(ref _dropped);

                return;
            }

            _pendingLine = line;
            _pendingError = error;
            _pendingFileLength = Copy(file, PendingFile);
            _pendingFunctionLength = Copy(function, PendingFunction);

            // The text can hold the marks of a format, such as %s, because the
            // arguments that fill them are the ones this handler does not read.
            _pendingTextLength = Copy(format, PendingText);

            // The lengths go before this line, thus a reader that sees Ready
            // sees the lengths that go with the bytes.
            Volatile.Write(ref _state, Ready);
        }
#pragma warning disable CA1031 // See the comment above.
        catch (Exception)
#pragma warning restore CA1031
        {
            // A message of a log is not a cause to stop the audio.
        }
    }

    /// <summary>
    /// Takes the bytes of a string of C into an array that the software made
    /// one time. It makes no memory, thus the audio thread can call it. A
    /// message that is longer than the array loses its end.
    /// </summary>
    private static unsafe int Copy(IntPtr text, byte[] destination)
    {
        if (text == IntPtr.Zero)
        {
            return 0;
        }

        ReadOnlySpan<byte> source =
            MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)text);

        int length = Math.Min(source.Length, destination.Length);

        source[..length].CopyTo(destination);

        return length;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "snd_lib_error_set_handler gave {result}. The messages of libasound stay on the standard error stream, and the journal takes them.")]
    private static partial void LogHandlerNotSet(ILogger logger, int result);

    // Drain makes the three strings, thus it tests the level itself and this
    // method must not test it a second time.
    [LoggerMessage(
        Level = LogLevel.Debug,
        SkipEnabledCheck = true,
        Message = "ALSA {file}:{line} ({function}) gave {error}: {text}")]
    private static partial void LogAlsa(
        ILogger logger,
        string file,
        int line,
        string function,
        int error,
        string text);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "{count} message(s) of libasound came while the one before them waited. The 4 lines of \"default\" repeat, thus each one says what the line above says.")]
    private static partial void LogDropped(ILogger logger, int count);
}
