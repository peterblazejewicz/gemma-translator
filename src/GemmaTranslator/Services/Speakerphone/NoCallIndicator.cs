// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services.Speakerphone;

/// <remarks>
/// Windows gives the HID device to its own driver stack, and it needs
/// <c>hid.dll</c> and a different call. The ring of the device is for the
/// appliance, thus the development host shows nothing.
/// </remarks>
public sealed partial class NoCallIndicator : ICallIndicator
{
    private readonly ILogger<NoCallIndicator> _logger;

    public NoCallIndicator(ILogger<NoCallIndicator> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <remarks>
    /// The line goes in the log because a development host that says nothing
    /// gives the same log as an appliance whose ring is dead.
    /// </remarks>
    public void Start() => LogNoIndicator(_logger);

    public void StartCall()
    {
    }

    public void EndCall()
    {
    }

    public void Dispose()
    {
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This machine shows no call indicator. The ring of the speakerphone is for the appliance.")]
    private static partial void LogNoIndicator(ILogger logger);
}
