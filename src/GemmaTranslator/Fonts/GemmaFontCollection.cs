// Copyright 2026 Google LLC
// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
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
//
// This file is part of a fork of google-gemma/gemma-translator and has
// been modified.

using Avalonia.Media.Fonts;

namespace GemmaTranslator.Fonts;

/// <summary>
/// The fonts that the software supplies with itself.
/// </summary>
/// <remarks>
/// CAUTION: Raspberry Pi OS Lite can have no font for Arabic and no font for
/// Chinese, Japanese, or Korean. Avalonia then throws
/// "Default font family name can't be null or empty" and the software does not
/// start. The software must not use a font of the operating system.
/// </remarks>
public sealed class GemmaFontCollection : EmbeddedFontCollection
{
    /// <summary>
    /// The scheme and the key of the collection, for a font family address.
    /// </summary>
    /// <remarks>
    /// The name is not <c>Key</c>, because <see cref="EmbeddedFontCollection"/>
    /// has a member with that name.
    /// </remarks>
    public const string CollectionUri = "fonts:GemmaTranslator";

    /// <summary>
    /// Initializes a new instance of the <see cref="GemmaFontCollection"/> class.
    /// </summary>
    public GemmaFontCollection()
        : base(
            new Uri(CollectionUri, UriKind.Absolute),
            new Uri("avares://GemmaTranslator/Assets/Fonts", UriKind.Absolute))
    {
    }
}
