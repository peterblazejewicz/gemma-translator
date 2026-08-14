# Copyright 2026 Google LLC
# Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.
#
# This file is part of a fork of google-gemma/gemma-translator and has been
# modified. The static files, /proxy and /api/volume went away, and /api/warm
# came in.

import http.server
import socketserver
import urllib.request
import os
import base64
import io
import json
import numpy as np
import wave
import traceback
import socket
import ssl

import threading
from collections import OrderedDict

# Multilingual STT via Moonshine.
# Language is fixed at recognizer construction, so we lazily build (and cache) one
# recognizer per language actually used.
# This server holds the speech-to-text part and the text-to-speech part, and
# nothing else. The static files, /proxy and /api/volume went away with
# frontend/: the user interface is the Avalonia software now, it needs no
# browser, it speaks to litert-lm directly, and the speakerphone has its own
# buttons for the volume.
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
SUPPORTED_STT_LANGS = {"en", "ar", "es", "ja", "zh", "ko"}
MAX_MODELS = 2
# Two lanes fill this cache with no free space left. A third language
# evicts the first one, and its next use starts a new copy of the model.
# Measured on the appliance: a request for Spanish evicted Japanese, and
# the next Japanese request then evicted English.
_stt_recognizers = OrderedDict()  # language -> recognizer
# RLock (reentrant): handle_stt holds the lock across get_stt_recognizer() + inference,
# and get_stt_recognizer() re-acquires it on the same thread. A plain Lock() self-deadlocks.
_stt_lock = threading.RLock()

# Multilingual TTS via moonshine-voice (Kokoro / Piper backed). Language is fixed at
# TextToSpeech construction, so we lazily build (and cache) one engine per language used.
# Maps our UI language codes -> moonshine-voice language codes.
TTS_LANG_MAP = {
    "ar": "ar-msa",
    "en": "en-us",
    "es": "es-es",
    "ja": "ja-jp",
    "zh": "zh-hans",
    "ko": "ko-kr",
}
# Optional per-language voice override (moonshine-voice voice IDs). Languages not
# listed here use moonshine's default voice for that language.
TTS_VOICE_MAP = {
    "zh": "kokoro_zf_xiaoxiao",  # 晓晓 — soft, gentle female Mandarin
}
_tts_engines = OrderedDict()  # our-lang-code -> TextToSpeech
# RLock (reentrant): handle_tts holds the lock across get_tts_engine() + synthesis,
# and get_tts_engine() re-acquires it on the same thread. A plain Lock() self-deadlocks.
_tts_lock = threading.RLock()

def get_tts_engine(language="en"):
    if language not in TTS_LANG_MAP:
        language = "en"
    with _tts_lock:
        if language in _tts_engines:
            _tts_engines.move_to_end(language)
            return _tts_engines[language]
        from moonshine_voice import TextToSpeech
        moon_lang = TTS_LANG_MAP[language]
        voice = TTS_VOICE_MAP.get(language)
        print(f"[TTS] Loading moonshine-voice (lang={language} -> {moon_lang}, voice={voice or 'default'})...")
        if len(_tts_engines) >= MAX_MODELS:
            oldest_lang, oldest_engine = _tts_engines.popitem(last=False)
            print(f"[TTS] Evicting model for {oldest_lang}")
            del oldest_engine
        if voice:
            _tts_engines[language] = TextToSpeech(moon_lang, voice=voice)
        else:
            _tts_engines[language] = TextToSpeech(moon_lang)
        return _tts_engines[language]

def get_stt_recognizer(language="en"):
    if language not in SUPPORTED_STT_LANGS:
        language = "en"
    with _stt_lock:
        if language in _stt_recognizers:
            _stt_recognizers.move_to_end(language)
            return _stt_recognizers[language]
        from moonshine_voice import get_model_for_language, Transcriber
        print(f"[STT] Loading Moonshine STT (lang={language})...")
        if len(_stt_recognizers) >= MAX_MODELS:
            oldest_lang, oldest_recognizer = _stt_recognizers.popitem(last=False)
            print(f"[STT] Evicting model for {oldest_lang}")
            del oldest_recognizer
        model_path, model_arch = get_model_for_language(language)
        _stt_recognizers[language] = Transcriber(model_path=model_path, model_arch=model_arch)
        return _stt_recognizers[language]


PORT = 3000

class ProxyHTTPRequestHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        # SECURITY CONTROL. Do not delete this override and do not make it
        # print the request line. BaseHTTPRequestHandler logs every request
        # line to stderr, and systemd puts stderr in the journal, which
        # persists across restarts. The request line of the text-to-speech
        # call is GET /api/tts?text=<the whole sentence a person said>.
        # Percent-encoding is not redaction: anybody who can read the journal
        # reads the sentence.
        #
        # Nothing diagnostic is lost. The C# client logs the status, the
        # duration and the size of every call, and a failure here still prints
        # its traceback below.
        pass

    def handle_tts(self):
        parsed_path = urllib.parse.urlparse(self.path)
        query = urllib.parse.parse_qs(parsed_path.query)
        text = query.get('text', [None])[0]
        lang = query.get('lang', ['en'])[0]

        if not text:
            self.send_response(400)
            self.end_headers()
            self.wfile.write(b'Error: Missing "text" parameter.')
            return

        # SECURITY CONTROL. Do not print the text. It is the translation of
        # what a person said, and this output is the journal of systemd.
        print(f"[TTS] Synthesizing with moonshine-voice: {len(text)} characters (lang: {lang})")

        try:
            with _tts_lock:
                engine = get_tts_engine(lang)
                audio, sample_rate = engine.synthesize(text)

            # moonshine-voice returns mono float samples in [-1, 1]; encode to 16-bit PCM WAV.
            samples = np.asarray(audio, dtype=np.float32)
            samples = np.clip(samples, -1.0, 1.0)
            pcm16 = (samples * 32767.0).astype('<i2')

            with io.BytesIO() as buf:
                with wave.open(buf, 'wb') as wf:
                    wf.setnchannels(1)
                    wf.setsampwidth(2)
                    wf.setframerate(int(sample_rate))
                    wf.writeframes(pcm16.tobytes())
                wav_bytes = buf.getvalue()

            self.send_response(200)
            self.send_header('Content-Type', 'audio/wav')
            self.send_header('Content-Length', str(len(wav_bytes)))
            self.end_headers()
            self.wfile.write(wav_bytes)
        except Exception as e:
            # SECURITY CONTROL. Print the type of the error and never str(e).
            # The synthesizer puts its input in the message when it fails on a
            # character, and that input is what a person said. This output goes
            # to the journal of systemd, and the journal keeps it after a
            # restart. traceback.print_exc() is safe: it gives the lines of the
            # source and not the values.
            traceback.print_exc()
            print(f"[TTS Error] {type(e).__name__}")
            self.send_response(500)
            self.end_headers()
            self.wfile.write(type(e).__name__.encode('utf-8'))

    def handle_stt(self):
        try:
            content_length = int(self.headers.get('Content-Length', 0))
            body = self.rfile.read(content_length)
            
            if not body:
                raise ValueError("No body data")
                
            data = json.loads(body.decode('utf-8'))
            audio_b64 = data.get('audio_base64')
            if not audio_b64:
                raise ValueError("Missing audio_base64 parameter")

            language = data.get('language', 'en')
            raw_data = base64.b64decode(audio_b64)
            
            # The browser sends a raw Float32Array buffer
            audio_np = np.frombuffer(raw_data, dtype=np.float32)

            with _stt_lock:
                recognizer = get_stt_recognizer(language)
                transcript = recognizer.transcribe_without_streaming(audio_np, 16000)
            text = " ".join([line.text for line in transcript.lines])
            # SECURITY CONTROL. Do not print the transcript. It is the words of
            # a person, and this output is the journal of systemd. The count of
            # the characters says that the recognizer gave an answer and it
            # keeps no speech.
            print(f"[STT] Transcribed: {len(text)} characters")

            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            self.wfile.write(json.dumps({"text": text}).encode('utf-8'))
        except Exception as e:
            # SECURITY CONTROL. The type of the error and never str(e). See the
            # same block in handle_tts: a recognizer that fails on the audio can
            # put a part of what it heard in the message, and this output is the
            # journal.
            traceback.print_exc()
            print(f"[STT Error] {type(e).__name__}")
            self.send_response(500)
            self.end_headers()
            self.wfile.write(type(e).__name__.encode('utf-8'))

    def handle_warm(self):
        parsed_path = urllib.parse.urlparse(self.path)
        query = urllib.parse.parse_qs(parsed_path.query)
        language = query.get('lang', [None])[0]

        # This code does not fall back to English. A caller that warms an
        # unknown code must learn that fact, and not get a false "all is
        # well".
        if not language or language not in SUPPORTED_STT_LANGS or language not in TTS_LANG_MAP:
            self.send_response(400)
            self.end_headers()
            self.wfile.write(b'Error: Unknown or missing "lang" parameter.')
            return

        try:
            # Each step uses only its own lock, the same as handle_stt and
            # handle_tts. The STT step and the TTS step run one after the
            # other, so this call never holds both locks at the same time.
            with _stt_lock:
                stt_loaded_now = language not in _stt_recognizers
                get_stt_recognizer(language)
            with _tts_lock:
                tts_loaded_now = language not in _tts_engines
                get_tts_engine(language)

            print(f"[Warm] lang={language} stt_loaded_now={stt_loaded_now} tts_loaded_now={tts_loaded_now}")

            body = json.dumps({
                "language": language,
                "stt": True,
                "tts": True,
                "stt_loaded_now": stt_loaded_now,
                "tts_loaded_now": tts_loaded_now,
            }).encode('utf-8')
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        except Exception as e:
            # The same control as the two handlers above. This one holds no text
            # of a person, and the three paths stay equal so that a person who
            # reads one of them learns the rule.
            traceback.print_exc()
            print(f"[Warm Error] {type(e).__name__}")
            self.send_response(500)
            self.end_headers()
            self.wfile.write(type(e).__name__.encode('utf-8'))

    def do_POST(self):
        if self.path.startswith('/api/stt'):
            self.handle_stt()
            return

        self.send_response(404)
        self.end_headers()

    def do_GET(self):
        if self.path.startswith('/api/tts'):
            self.handle_tts()
            return
        if self.path.startswith('/api/warm'):
            self.handle_warm()
            return

        self.send_response(404)
        self.end_headers()

if __name__ == '__main__':
    # Allow port reuse
    socketserver.TCPServer.allow_reuse_address = True
    local_ip = "localhost"
    try:
        # Create a dummy socket to find local network IP
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        local_ip = s.getsockname()[0]
        s.close()
    except Exception:
        pass

    use_ssl = os.path.exists('cert.pem') and os.path.exists('key.pem')

    # SECURITY CONTROL. Do not change "127.0.0.1" back to "" or to "0.0.0.0".
    #
    # "" is INADDR_ANY: it accepts a connection from any machine that can reach
    # this one. This server takes the recorded voice of a person on /api/stt,
    # and it has no password, no token and no test of the caller. Upstream bound
    # this way because a browser on the network was the client. That browser is
    # gone: the only client is the Avalonia software on this same machine, and
    # its address is checked to be a loopback address.
    #
    # What "" gives anybody on the same network: a GET of /api/warm holds the
    # one lock of the speech-to-text part for about 6 seconds while it makes a
    # model, thus a loop of them makes the appliance answer nobody; a POST to
    # /api/stt with a large Content-Length takes the memory of a machine that
    # has 4 GB and a model of 2.4 GB in it; and each call uses the processor and
    # the cells.
    with socketserver.ThreadingTCPServer(("127.0.0.1", PORT), ProxyHTTPRequestHandler) as httpd:
        if use_ssl:
            context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
            context.load_cert_chain(certfile='cert.pem', keyfile='key.pem')
            httpd.socket = context.wrap_socket(httpd.socket, server_side=True)

        protocol = "https" if use_ssl else "http"
        print(f"===========================================================")
        print(f"LiteRT-LM Audio Testbed client running at:")
        print(f"👉 {protocol}://localhost:{PORT}")
        if local_ip != "localhost":
            print(f"👉 {protocol}://{local_ip}:{PORT} (Local Network)")
        print(f"===========================================================")
        def _prewarm_models():
            try:
                # GEMMA_PREWARM_LANGS names the languages to warm at
                # startup, separated by a comma, for example "en,ja". With
                # no value set, only English starts, the same as before
                # this option existed.
                #
                # Each name is checked here, for the same reason that
                # handle_warm answers 400: get_stt_recognizer and
                # get_tts_engine fall back to English for a name they do not
                # know, so "de,fr" would print that it loaded German and
                # French and load English two times. MAX_MODELS is 2, so more
                # than two names leaves only the last two loaded.
                raw_langs = os.environ.get('GEMMA_PREWARM_LANGS', 'en')
                wanted = [lang.strip() for lang in raw_langs.split(',') if lang.strip()]
                langs = []
                skipped = []
                for lang in wanted:
                    if lang in SUPPORTED_STT_LANGS and lang in TTS_LANG_MAP:
                        langs.append(lang)
                    else:
                        skipped.append(lang)
                if skipped:
                    print(f"[Prewarm] Unknown language(s), not loaded: {', '.join(skipped)}", flush=True)
                if not langs:
                    langs = ['en']
                print(f"[Prewarm] Loading STT & TTS models for: {', '.join(langs)}...", flush=True)
                for lang in langs:
                    get_stt_recognizer(lang)
                    get_tts_engine(lang)
                print("[Prewarm] Models pre-warmed successfully.", flush=True)
            except Exception as e:
                print(f"[Prewarm Error] {e}", flush=True)

        threading.Thread(target=_prewarm_models, daemon=True).start()
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nShutting down server.")
