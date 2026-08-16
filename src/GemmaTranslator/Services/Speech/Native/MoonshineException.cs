// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace GemmaTranslator.Services.Speech.Native;

/// <remarks>
/// The message comes from <c>moonshine_error_to_string</c>, which maps a code
/// to a text of the library. It holds no part of what a person said, thus a
/// log line may show it. Do not add the text of the caller to this message.
/// </remarks>
internal sealed class MoonshineException : Exception
{
    private MoonshineException(int code, string message)
        : base(message) => Code = code;

    public int Code { get; }

    /// <summary>
    /// CAUTION: a load function gives the handle and the error code in one
    /// <c>int32</c>. A value of 0 or more is a handle, and a handle of 0 is
    /// valid.
    /// </summary>
    public static int Handle(int result, string operation) => result < 0
        ? throw Make(result, operation)
        : result;

    /// <summary>Throws if <paramref name="result"/> is not 0.</summary>
    public static void Check(int result, string operation)
    {
        if (result != 0)
        {
            throw Make(result, operation);
        }
    }

    private static MoonshineException Make(int code, string operation)
    {
        string? text = Marshal.PtrToStringUTF8(MoonshineLibrary.ErrorToString(code));

        return new MoonshineException(
            code,
            string.IsNullOrWhiteSpace(text)
                ? $"{operation} gave error {code}."
                : $"{operation} gave error {code}: {text}");
    }
}
