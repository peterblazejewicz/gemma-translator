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

import React, { useState, useEffect, useRef, useCallback } from "react"
import LanguageLane from "./components/LanguageLane"
import ResponseDrawer from "./components/ResponseDrawer"
import Visualizer from "./components/Visualizer"
import { transcribeAudio, splitTextIntoSpeechChunks } from "./utils/api"
import { playBlip } from "./utils/audio-blip"

// Core orchestrator for the two-person kiosk translator.
//
// The microphone, the two buttons, and the translation have moved to C#. Two
// physical buttons drive the appliance now, so the keyboard handlers and the
// "landscape" active-person mode are gone: that mode existed only because one
// keyboard had to serve two people.
//
// What is left here is speech-to-text and text-to-speech. Nothing calls them,
// because the audio that fed them is now captured in C#. CLAUDE.md section 5.3
// keeps this code until those two slices land; then this file is deleted.
//
// CAUTION: a third part is still here. Lane 1 and lane 2 rotate their language
// from the two arrows on the display: handleRotateLanguage below, through the
// onRotate of LanguageLane. No C# replaces that. Do not delete this file for
// the two slices alone.

// Languages offered on each lane's revolver; ttsLang selects the backend voice.
const AVAILABLE_LANGUAGES = [
  { code: "ar", name: "Arabic", voice: "tts", ttsLang: "ar" },
  { code: "en", name: "English", voice: "tts", ttsLang: "en" },
  { code: "es", name: "Spanish", voice: "tts", ttsLang: "es" },
  { code: "ja", name: "Japanese", voice: "tts", ttsLang: "ja" },
  { code: "zh", name: "Chinese", voice: "tts", ttsLang: "zh" },
  { code: "ko", name: "Korean", voice: "tts", ttsLang: "ko" },
]

function TranslatorApp({ config }) {
  // UI State
  const [isDrawerOpen, setIsDrawerOpen] = useState(false)

  // Translation State
  const [transcriptionData, setTranscriptionData] = useState({
    source: "",
    text: "— listening —",
  })
  const [translationData, setTranslationData] = useState({
    target: "",
    text: "— waiting —",
  })
  const [metaText, setMetaText] = useState("")

  // Currently-playing TTS audio element (chunked playback chain)
  const onlineAudioPlayerRef = useRef(null)

  // Language Lanes State
  const [lang1Index, setLang1Index] = useState(0)
  const [lang2Index, setLang2Index] = useState(1)

  const stopSpeaking = useCallback(() => {
    if (onlineAudioPlayerRef.current) {
      onlineAudioPlayerRef.current.pause()
      onlineAudioPlayerRef.current = null
    }
  }, [])

  // Speak text via /api/tts, splitting into ~180-char chunks and chaining
  // playback so long translations don't overflow a single TTS request.
  //
  // Nothing calls this right now: translation moved to C# and TTS ran on its
  // output. Text-to-speech has no C# replacement yet, so CLAUDE.md section 5.3
  // says the upstream code stays until that slice lands. Do not delete it.
  const playTTS = useCallback(
    (text, targetLang) => {
      if (!text) return
      stopSpeaking()

      const chunks = splitTextIntoSpeechChunks(text)
      if (chunks.length === 0) return

      let chunkIndex = 0

      const playNextChunk = () => {
        if (chunkIndex >= chunks.length) {
          stopSpeaking()
          return
        }
        const ttsUrl = `/api/tts?text=${encodeURIComponent(chunks[chunkIndex])}&lang=${encodeURIComponent(targetLang)}`
        const player = new Audio(ttsUrl)
        player.volume = 1.0
        onlineAudioPlayerRef.current = player

        player.onended = () => {
          chunkIndex++
          playNextChunk()
        }
        player.onerror = () => {
          stopSpeaking()
          alert("TTS playback failed. Backend server may be offline.")
        }
        player.play().catch((e) => {
          console.error("Audio play error:", e)
          stopSpeaking()
        })
      }

      playNextChunk()
    },
    [stopSpeaking],
  )

  // Rotate a lane's language, skipping the slot held by the other lane
  // (the two lanes may never show the same language).
  const handleRotateLanguage = useCallback(
    (lane, direction) => {
      const N = AVAILABLE_LANGUAGES.length

      playBlip("language")

      if (lane === 1) {
        let ni = (lang1Index + direction + N) % N
        if (ni === lang2Index) ni = (ni + direction + N) % N
        setLang1Index(ni)
      } else {
        let ni = (lang2Index + direction + N) % N
        if (ni === lang1Index) ni = (ni + direction + N) % N
        setLang2Index(ni)
      }
    },
    [lang1Index, lang2Index],
  )

  // Translation Pipeline
  const processTranslation = async (lane, base64Data) => {
    setIsDrawerOpen(true)

    const src =
      lane === 1
        ? AVAILABLE_LANGUAGES[lang1Index]
        : AVAILABLE_LANGUAGES[lang2Index]
    const dst =
      lane === 1
        ? AVAILABLE_LANGUAGES[lang2Index]
        : AVAILABLE_LANGUAGES[lang1Index]

    setTranscriptionData({
      source: `${src.name} (Source)`,
      text: "Analyzing voice input...",
    })
    setTranslationData({
      target: `${dst.name} (Translation)`,
      text: "Translating...",
    })
    setMetaText("")

    try {
      // 1. Transcription
      setTranscriptionData((prev) => ({ ...prev, text: "Listening..." }))
      const transcribedText = await transcribeAudio(base64Data, src.code)
      setTranscriptionData((prev) => ({ ...prev, text: transcribedText }))

      if (!transcribedText.trim()) {
        setTranslationData((prev) => ({
          ...prev,
          text: "(No speech detected)",
        }))
        return
      }

      // 2. Translation — moved to C#.
      //
      // GemmaTranslator.Services.LiteRtTranslator does this step now, and it
      // owns the system prompt. This React path keeps speech-to-text only
      // until the audio and STT slices move too, and then this whole file is
      // deleted.
      setTranslationData((prev) => ({
        ...prev,
        text: "(Translation moved to the C# software)",
      }))
      setMetaText("")
    } catch (err) {
      console.error(err)
      setTranscriptionData((prev) => ({
        ...prev,
        text: prev.text === "Listening..." ? "(Transcription failed)" : prev.text,
      }))
      setTranslationData((prev) => ({ ...prev, text: `Error: ${err.message}` }))
    }
  }

  return (
    <div className="translator-envelope">
      <ResponseDrawer
        isActive={isDrawerOpen}
        onClose={() => setIsDrawerOpen(false)}
        transcriptionSource={transcriptionData.source}
        transcriptionText={transcriptionData.text}
        translationTarget={translationData.target}
        translationText={translationData.text}
        metaText={metaText}
      />

      <main className="translator-workspace">
        <div className="languages-container">
          <LanguageLane
            laneId={1}
            laneLabel="1"
            languages={AVAILABLE_LANGUAGES}
            currentIndex={lang1Index}
            isRecording={false}
            isActivePerson={false}
            onRotate={(dir) => handleRotateLanguage(1, dir)}
          />
          <LanguageLane
            laneId={2}
            laneLabel="2"
            languages={AVAILABLE_LANGUAGES}
            currentIndex={lang2Index}
            isRecording={false}
            isActivePerson={false}
            onRotate={(dir) => handleRotateLanguage(2, dir)}
          />
        </div>

        {/* The visualizer has no audio source now: the capture is in C# and
            the Web Audio AnalyserNode went with the recorder hook. The C#
            visualizer needs an FFT that we write. See CLAUDE.md. */}
        <Visualizer
          activePerson={1}
          isRecording={false}
          analyser={null}
          barsCount={parseInt(config.visualizerBars, 10)}
        />
      </main>
    </div>
  )
}

export default TranslatorApp
