// This is the main user-facing component (ie MonoBehaviour)
// acts as a bridge between Unity and BizHawkInstance, handling input,
// graphics, audio, multi-threading, and framerate throttling.

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

#if UNITY_EDITOR
using UnityEditor;
#endif

using NaughtyAttributes;

using UnityEngine;
using Unity.Profiling;

using BizHawk.Client.Common; // Only for the PlunderludicSample logic [should maybe be in a different namespace, idk]

namespace UnityHawk {

// [is Emulator the right name for this? i guess so?]
public class Emulator : MonoBehaviour
{
    public bool useAttachedRenderer = true;
    [HideIf("useAttachedRenderer")]
    public Renderer targetRenderer;

    [Header("Files")]
    // All pathnames are loaded relative to ./StreamingAssets/, unless the pathname is absolute (see GetAbsolutePath)
    public string romFileName = "Roms/mario.nes";
    public string configFileName = ""; // Leave empty for default config.ini
    public string saveStateFileName = ""; // Leave empty to boot clean
    public List<string> luaScripts;

    [Header("Params")]
    public float frameRateMultiplier = 1f; // Speed up or slow down emulation

    [Foldout("Debug")] public bool disableMultithreadingForDebug = false; // Run everything on the main unity thread - not really recommended but useful for debugging sometimes
    [Foldout("Debug"), ReadOnly] public int frame = 0;
    [Foldout("Debug"), ReadOnly] public float uncappedFps;
    [Foldout("Debug"), ReadOnly] public float emulatorDefaultFps;
    [Foldout("Debug"), ReadOnly] public string currentCore = "nul";

    // [Make these public for debugging texture stuff]
    private TextureFormat textureFormat = TextureFormat.BGRA32;
    private RenderTextureFormat renderTextureFormat = RenderTextureFormat.BGRA32;
    private bool linearTexture; // [seems so make no difference visually]
    private bool forceReinitTexture;
    private bool blitTexture = true;
    private bool doTextureCorrection = true;

    // [Make these public for debugging audio stuff]
    private bool useManualAudioHandling = false;
    public enum AudioStretchMethod {Truncate, PreserveSampleRate, Stretch, Overlap}
    private AudioStretchMethod audioStretchMethod = AudioStretchMethod.Truncate; // if using non-manual audio, this should probably be Truncate

    // Interface for other scripts to use
    public RenderTexture Texture => _renderTexture;
    public bool IsRunning => _bizHawk?.IsLoaded ?? false;

    private BizHawkInstance _bizHawk;
    InputProvider inputProvider;

    Texture2D _bufferTexture;
    RenderTexture _renderTexture; // We have to maintain a separate rendertexture just for the purpose of flipping the image we get from the emulator

    static int AudioChunkSize = 734; // [Hard to explain right now but for 'Accumulate' AudioStretchMethod, preserve audio chunks of this many samples (734 ~= 1 frame at 60fps at SR=44100)]
    static int AudioBufferSize = 44100*2;

    short[] audioBuffer; // circular buffer (queue) to store audio samples accumulated from the emulator
    int audioBufferStart, audioBufferEnd;
    static int ChannelCount = 2; // Seems to be always 2, for all BizHawk sound and for Unity audiosource
    private int audioSamplesNeeded; // track how many samples unity wants to consume

    ProfilerMarker s_FrameAdvanceMarker;

    bool _stopEmulatorTask = false;

    static readonly string textureCorrectionShaderName = "TextureCorrection";
    Material _textureCorrectionMat;

#if UNITY_EDITOR
    // Set filename fields based on sample directory
    [Button(enabledMode: EButtonEnableMode.Editor)]
    private void LoadSample() {
        string path = EditorUtility.OpenFilePanel("Sample", "", "");
        if (!String.IsNullOrEmpty(path)) {
            SetFromSample(path);
        }
    }
#endif

    // Returns the path that will be loaded for a filename param (rom, lua, config, savestate)
    public static string GetAbsolutePath(string path) {
        if (path == Path.GetFullPath(path)) {
            // Already an absolute path, don't change it [Path.Combine below will do this anyway but just to be explicit]
            return path;
        } else {
            return Path.Combine(Application.streamingAssetsPath, path); // Load relative to StreamingAssets/
        }
    }

    void OnEnable()
    {
        _textureCorrectionMat = new Material(Resources.Load<Shader>(textureCorrectionShaderName));
        // Initialize stuff
        inputProvider = new InputProvider();

        audioBuffer = new short[AudioBufferSize];
        audioSamplesNeeded = 0;
        ClearAudioBuffer();

        _bizHawk = new BizHawkInstance();

        s_FrameAdvanceMarker = new ProfilerMarker($"FrameAdvance {GetInstanceID()}");

        if (useAttachedRenderer) {
            // Default to the attached Renderer component, if there is one
            targetRenderer = GetComponent<Renderer>();
            if (!targetRenderer) {
                Debug.LogWarning("No Renderer attached, will not display emulator graphics");
            }
        }

        // Check if there is an AudioSource attached
        // [would be nice if we could specify a targetAudioSource on a different object but Unity makes that inconvenient]
        if (!GetComponent<AudioSource>()) {
            Debug.LogWarning("No AudioSource component, will not play emulator audio");
        }

        _stopEmulatorTask = false;

        // Process filename args
        string configPath;
        if (String.IsNullOrEmpty(configFileName)) {
            configPath = UnityHawk.defaultConfigPath;
        } else {
            configPath = GetAbsolutePath(configFileName);
        }

        string romPath = GetAbsolutePath(romFileName);

        var luaScriptPaths = luaScripts.Select(path => GetAbsolutePath(path)).ToList();

        string saveStateFullPath = null;
        if (!String.IsNullOrEmpty(saveStateFileName)) {
            saveStateFullPath = GetAbsolutePath(saveStateFileName);
        }

        // Load the emulator + rom
        bool loaded = _bizHawk.InitEmulator(
            configPath,
            romPath,
            luaScriptPaths,
            saveStateFullPath
        );
        if (loaded) {
            currentCore = _bizHawk.CurrentCoreName;
            InitTextures();

            // start emulator looping in a new thread unrelated to the unity framerate
            // [maybe should have a param to set if it should run in a separate thread or not? kind of annoying to support both though]
            if (!disableMultithreadingForDebug) {
                Task.Run(EmulatorLoop);
            }
        }
    }

    // For editor convenience: Set filename fields by reading a sample directory
    public void SetFromSample(string samplePath) {
        // Read the sample dir to get the necessary filenames (rom, config, etc)
        PlunderludicSample s = PlunderludicSample.LoadFromDir(samplePath);
        romFileName = s.romPath;
        configFileName = s.configPath;
        saveStateFileName = s.saveStatePath;
        luaScripts = s.luaScriptPaths.ToList();
    }

    void Update() {
        if (disableMultithreadingForDebug) {
            // For debug, run the FrameAdvance in the main update thread
            EmulatorStep();
        }

        if (_bizHawk.IsLoaded) {
            // replenish the input queue with new input from unity
            inputProvider.Update();
            // get the texture from bizhawk and blit to the unity texture
            // [for efficiency we could have a flag to check if the texture has changed since last Update (it won't have if the emulator is running slower than unity)]
            UpdateTexture();
        }
    }
    
    void OnDisable() {
        // In the editor, gotta kill the task or it will keep running in edit mode
        _stopEmulatorTask = true;
    }

    void EmulatorLoop() {
        while (!_stopEmulatorTask) {
            EmulatorStep();
        }
    }

    // This will run asynchronously so that it's not bound by unity update framerate
    // (unless disableMultithreadingForDebug is set)
    void EmulatorStep() {
        if (!_bizHawk.IsLoaded) return;

        emulatorDefaultFps = (float)_bizHawk.DefaultFrameRate; // Idk if this can change at runtime but checking every frame just in case
        System.Diagnostics.Stopwatch sw = new();
        sw.Start();
        s_FrameAdvanceMarker.Begin();

        _bizHawk.FrameAdvance(inputProvider);

        // Store audio from the last emulated frame so it can be played back on the unity audio thread
        StoreLastFrameAudio();

        s_FrameAdvanceMarker.End();
        sw.Stop();
        TimeSpan ts = sw.Elapsed;
        uncappedFps = (float)(1f/ts.TotalSeconds);

        // Very naive throttle but it works fine - just sleep a bit if above target fps on this frame
        // (Throttle.cs in BizHawk seems a lot more sophisticated)
        float targetFps = emulatorDefaultFps*frameRateMultiplier;
        if (uncappedFps > targetFps) {
            Thread.Sleep((int)(1000*(1f/targetFps - 1f/uncappedFps)));
        }
        
        frame++;
    }

    void UpdateTexture() {
        // Re-init the target texture if needed (if dimensions have changed, as happens on PSX)
        if (forceReinitTexture || !_bufferTexture || !_renderTexture || (_bufferTexture.width != _bizHawk.VideoBufferWidth || _bufferTexture.height != _bizHawk.VideoBufferHeight)) {
            InitTextures();
            forceReinitTexture = false;
        }

        if (blitTexture) {
            // Copy the texture from the emulator into a Unity texture
            int[] videoBuffer = _bizHawk.GetVideoBuffer();
            int nPixels = _bufferTexture.width * _bufferTexture.height;
            // Debug.Log($"Actual video buffer size: {videoBuffer.Length}");
            // Debug.Log($"Number of pixels: {nPixels}");

            // Write the pixel data from the emulator directly into the bufferTexture
            // (seems to basically work as long as the texture format is BGRA32)
            //  with two minor problems: it's flipped vertically & alpha channel is set to 0
            _bufferTexture.SetPixelData(videoBuffer, 0);
            _bufferTexture.Apply(/*updateMipmaps: false*/);

            if (doTextureCorrection) {
                // Correct issues with the texture by applying a shader and blitting to a separate render texture:
                Graphics.Blit(_bufferTexture, _renderTexture, _textureCorrectionMat, 0);
            }
        }
    }

    // Init/re-init the textures for rendering the screen - has to be done whenever the source dimensions change (which happens often on PSX for some reason)
    void InitTextures() {
        _bufferTexture = new     Texture2D(_bizHawk.VideoBufferWidth, _bizHawk.VideoBufferHeight, textureFormat, linearTexture);
        _renderTexture = new RenderTexture(_bizHawk.VideoBufferWidth, _bizHawk.VideoBufferHeight, depth:0, format:renderTextureFormat);
        if (targetRenderer) targetRenderer.material.mainTexture = _renderTexture;
    }

    void StoreLastFrameAudio() {
        // get audio samples for the emulated frame
        short[] lastFrameAudioBuffer;
        int nSamples;
        if (useManualAudioHandling) {
            nSamples = 0;
            _bizHawk.GetSamplesSync(out lastFrameAudioBuffer, out nSamples);
            // NOTE! there are actually 2*nSamples values in the buffer because it's stereo sound
            // Debug.Log($"Adding {nSamples} samples to the buffer.");
            // [Seems to be ~734 samples each frame for mario.nes]
            // append them to running buffer
        } else {
            nSamples = audioSamplesNeeded;
            lastFrameAudioBuffer = new short[audioSamplesNeeded*ChannelCount];
            _bizHawk.GetSamples(lastFrameAudioBuffer);
            audioSamplesNeeded = 0;
        }

        lock (audioBuffer) {
            for (int i = 0; i < nSamples*ChannelCount; i++) {
                if (AudioBufferLength() == audioBuffer.Length - 1) {
                    // Debug.LogWarning("audio buffer full, dropping samples");
                    break;
                }
                audioBuffer[audioBufferEnd] = lastFrameAudioBuffer[i];
                audioBufferEnd++;
                audioBufferEnd %= audioBuffer.Length;
            }
        }
    }

    // Send audio from the emulator to the AudioSource
    // (will only run if there is an AudioSource component attached)
    void OnAudioFilterRead(float[] out_buffer, int channels) {
        if (channels != ChannelCount) {
            Debug.LogError("AudioSource must be set to 2 channels");
            return;
        }
        // for non-manual audio - track how many samples we wanna request from bizhawk
        audioSamplesNeeded += out_buffer.Length/channels;

        // copy from the accumulated emulator audio buffer into unity's buffer
        // this needs to happen in both manual and non-manual mode
        lock (audioBuffer) {
            int n_samples = AudioBufferLength();
            // Debug.Log($"Unity buffer size: {out_buffer.Length}; Emulated audio buffer size: {n_samples}");

            // Unity needs 2048 samples right now, and depending on the speed the emulator is running,
            // we might have anywhere from 0 to like 10000 accumulated.
            
            // If the emulator isn't running at 'native' speed (e.g running at 0.5x or 2x), we need to do some kind of rudimentary timestretching
            // to play the audio faster/slower without distorting too much

            if (audioStretchMethod == AudioStretchMethod.PreserveSampleRate) {
                // Play back the samples at 1:1 sample rate, which means audio will lag behind if the emulator runs faster than 1x
                for (int out_i = 0; out_i < out_buffer.Length; out_i++) {
                    if (AudioBufferLength() == 0) {
                        Debug.LogWarning("Emulator audio buffer has no samples to consume");
                        return;
                    }
                    out_buffer[out_i] = audioBuffer[audioBufferStart]/32767f;
                    audioBufferStart = (audioBufferStart+1)%audioBuffer.Length;
                }
                // leave remaining samples for next time
            } else if (audioStretchMethod == AudioStretchMethod.Overlap) {
                // experimental attempt to do pitch-neutral timestretching by preserving the sample rate of audio chunks of a certain length (AudioChunkSize)
                // but playing those chunks either overlapping (if emulator is faster than native speed) or with gaps (if slower)
                // [there may be better ways to do this]
                // (it seems like EmuHawk does something similar to this maybe?)
                // [currently sounds really bad i think there must be something wrong with the code below]
                    
                int n_chunks = (n_samples-1)/AudioChunkSize + 1;
                int chunk_sep = (out_buffer.Length - AudioChunkSize)/n_chunks;
                
                for (int out_i = 0; out_i < out_buffer.Length; out_i++ ) {
                    out_buffer[out_i] = 0f;

                    // Add contribution from each chunk
                    // [might be better to take the mean of all chunks here, idk.]
                    for (int chunk_i = 0; chunk_i < n_chunks; chunk_i++) {
                        int chunk_start = chunk_i*chunk_sep; // in output space
                        if (chunk_start <= out_i && out_i < chunk_start + AudioChunkSize) {
                            // This chunk is contributing
                            int src_i = (out_i - chunk_start) + (chunk_i*AudioChunkSize);
                            short sample = GetAudioBufferAt(src_i);
                            out_buffer[out_i] += sample/32767f; // convert short (-32768 to 32767) to float (-1f to 1f)
                        }
                    }
                }
                ClearAudioBuffer(); // all samples consumed
            } else if (audioStretchMethod == AudioStretchMethod.Stretch) {
                // No pitch adjustment, just stretch the accumulated audio to fit unity's audio buffer
                // [sounds ok but a little weird, and means the pitch changes if the emulator framerate changes]
                // [although in reality it doesn't actually sound like it's getting stretched, just distorted, i don't understand this]
        
                for (int out_i = 0; out_i < out_buffer.Length; out_i++) {
                    int src_i = (out_i*n_samples)/out_buffer.Length;
                    out_buffer[out_i] = GetAudioBufferAt(src_i)/32767f;
                }
                ClearAudioBuffer(); // all samples consumed
            } else if (audioStretchMethod == AudioStretchMethod.Truncate) {
                // very naive, just truncate incoming samples if necessary
                // [sounds ok but very distorted]
                for (int out_i = 0; out_i < out_buffer.Length; out_i++) {
                    out_buffer[out_i] = GetAudioBufferAt(out_i)/32767f;
                }
                ClearAudioBuffer(); // throw away any remaining samples
            } else {
                Debug.LogWarning("Unhandled AudioStretchMode");
            }
        }
    }

    // helper methods for circular audio buffer
    // [could also just make an external CircularBuffer class]
    private int AudioBufferLength() {
        return (audioBufferEnd-audioBufferStart+audioBuffer.Length)%audioBuffer.Length;
    }
    private void ClearAudioBuffer() {
        audioBufferStart = 0;
        audioBufferEnd = 0;
    }
    private short GetAudioBufferAt(int i) {
        return audioBuffer[(audioBufferStart + i)%audioBuffer.Length];
    }
}
}