// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using GemmaTranslator.Configuration;
using GemmaTranslator.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GemmaTranslator;

/// <summary>
/// The one location that makes the settings and registers each service of the
/// software.
/// </summary>
/// <remarks>
/// Section 3.2 of CLAUDE.md makes this a rule. Do not make a service or a view
/// model with <c>new</c> in a view. Get it from the container, and give each
/// dependency to the constructor.
/// </remarks>
public static class ServiceRegistration
{
    /// <summary>
    /// Reads the settings of the software.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base path is <see cref="AppContext.BaseDirectory"/> and not the
    /// current directory. systemd starts the software with a current directory
    /// of <c>/</c>, thus the current directory finds no file.
    /// </para>
    /// <para>
    /// The file is optional, and thus the software starts with no file. The
    /// environment comes after the file and can change each value. A variable
    /// has the prefix <c>GEMMA_</c>, for example
    /// <c>GEMMA_Logging__LogLevel__Default</c>.
    /// </para>
    /// </remarks>
    /// <returns>The settings.</returns>
    public static IConfiguration BuildConfiguration()
        => new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("GEMMA_")
            .Build();

    /// <summary>
    /// Adds each service of the software to the collection.
    /// </summary>
    /// <param name="services">The collection of the container.</param>
    /// <param name="configuration">The settings from <see cref="BuildConfiguration"/>.</param>
    /// <returns>The same collection, for a chain of calls.</returns>
    public static IServiceCollection AddGemmaTranslator(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(configuration);

        services.AddLogging(logging =>
        {
            // The levels come from the "Logging" section of appsettings.json.
            logging.AddConfiguration(configuration.GetSection("Logging"));

            // The Raspberry Pi gets the console, which systemd puts in the
            // journal. Windows gets the debug output, because a WinExe has no
            // console and the console lines go to no location.
            logging.AddConsole();
            logging.AddDebug();
        });

        // The settings of the LiteRT-LM server. The values come from the
        // "LiteRt" section, and the validator examines them.
        services.AddOptions<LiteRtOptions>()
            .Bind(configuration.GetSection(LiteRtOptions.SectionName));

        services.AddSingleton<IValidateOptions<LiteRtOptions>, LiteRtOptionsValidator>();

        // The view models.
        services.AddSingleton<MainViewModel>();

        // The parts that touch the hardware and the machine come later: the
        // audio capture, the speech-to-text part, the translation part, and
        // the text-to-speech part.
        //
        // Each one gets an interface, because Windows and the Raspberry Pi get
        // different code and a different native library. See section 5.2 of
        // CLAUDE.md.
        return services;
    }
}
