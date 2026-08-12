// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services;

/// <summary>
/// The electrical supply on a machine that has no X1201.
/// </summary>
/// <remarks>
/// CAUTION: this class gives no value and it makes no value. A class that says
/// "the mains is on and the charge is 100" gives a log on the development host
/// that looks the same as a log of an appliance that operates. Then a defect
/// of the Raspberry Pi has no signal.
/// </remarks>
public sealed partial class NoPowerMonitor : IPowerMonitor
{
    private readonly ILogger<NoPowerMonitor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NoPowerMonitor"/> class.
    /// </summary>
    /// <param name="logger">The logger from the container.</param>
    public NoPowerMonitor(ILogger<NoPowerMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public PowerState Current { get; } = new(null, null);

    /// <inheritdoc/>
    public void Start() => LogNoSupply(_logger);

    /// <inheritdoc/>
    public void Dispose()
    {
        // There is nothing to stop.
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This machine has no X1201. The software reads no mains line and no fuel gauge.")]
    private static partial void LogNoSupply(ILogger logger);
}
