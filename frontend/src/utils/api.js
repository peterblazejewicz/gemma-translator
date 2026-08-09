/**
 * Copyright 2026 Google LLC
 * Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * This file is part of a fork of google-gemma/gemma-translator and has
 * been modified.
 */

// API client for the Python backend (backend/server.py).
//
// The LLM calls have moved to C#. GemmaTranslator.Services.LiteRtTranslator
// speaks to the LiteRT-LM server directly, and LiteRtOptions.GetBaseUrl()
// replaces getNormalizedBaseUrl. C# has no browser and no same-origin rule,
// so the /proxy route is not needed there.
//
// What is left here is speech-to-text and text-to-speech. Those slices are
// still in Python and React.

// POST base64 Float32 PCM (16 kHz mono) to the local Moonshine STT.
export async function transcribeAudio(base64Data, sourceLangCode) {
  const response = await fetch("/api/stt", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      audio_base64: base64Data,
      language: sourceLangCode,
    }),
  })

  if (!response.ok) {
    throw new Error(`STT failed: ${response.status}`)
  }

  const sttData = await response.json()
  return sttData.text || ""
}

// Word-safe chunking so each /api/tts request stays under ~`limit` chars.
export function splitTextIntoSpeechChunks(text, limit = 180) {
  const words = text.split(/\s+/)
  const chunks = []
  let currentChunk = ""
  for (const word of words) {
    if ((currentChunk + " " + word).trim().length <= limit) {
      currentChunk = (currentChunk + " " + word).trim()
    } else {
      if (currentChunk) chunks.push(currentChunk)
      currentChunk = word
    }
  }
  if (currentChunk) chunks.push(currentChunk)
  return chunks
}

