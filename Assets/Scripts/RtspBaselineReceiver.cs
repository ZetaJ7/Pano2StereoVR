using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Pano2StereoVR
{
    public sealed class RtspBaselineReceiver : MonoBehaviour
    {
        [SerializeField] private string ffmpegExecutable = "ffmpeg";
        [SerializeField] private string ffprobeExecutable = string.Empty;
        [SerializeField] private string rtspUrl = string.Empty;
        [SerializeField] [Min(16)] private int outputWidth = 1920;
        [SerializeField] [Min(16)] private int outputHeight = 1080;
        [SerializeField] [Min(1048576)] private int maxFrameBytes = 134217728;
        [SerializeField] private bool autoStartOnEnable = true;
        [SerializeField] private bool preferTcpTransport = true;
        [SerializeField] [Min(0f)] private float maxDecodeFps = 0f;
        [SerializeField] [Min(100)] private int reconnectDelayMs = 1000;
        [SerializeField] private bool enableLowLatencyInputOptions = true;
        [SerializeField] private bool useDirectIo = true;
        [SerializeField] [Min(0)] private int probeSizeBytes = 32768;
        [SerializeField] [Min(0)] private int analyzeDurationUs = 0;
        [SerializeField] [Min(0)] private int maxDelayUs = 0;
        [SerializeField] [Min(0)] private int reorderQueueSize = 0;
        [SerializeField] private bool autoDetectInputFps = true;
        [SerializeField] [Min(1f)] private float fallbackInputFps = 60f;
        [SerializeField] [Min(100)] private int ffprobeTimeoutMs = 1500;
        [SerializeField] private bool verboseFfmpegLog = false;
        [SerializeField] private bool allowRuntimeOverrides = true;
        [SerializeField] private string rtspUrlArgName = "--rtsp-url";
        [SerializeField] private string ffmpegExecutableArgName = "--ffmpeg-exe";
        [SerializeField] private string ffprobeExecutableArgName = "--ffprobe-exe";
        [SerializeField] private string rtspUrlEnvName = "P2SVR_RTSP_URL";
        [SerializeField] private string ffmpegExecutableEnvName = "P2SVR_FFMPEG_EXE";
        [SerializeField] private string ffprobeExecutableEnvName = "P2SVR_FFPROBE_EXE";

        private readonly object _stateLock = new object();
        private readonly object _processLock = new object();
        private const int PipePeekUnavailableBytes = -1;
        private Process _ffmpegProcess;
        private Thread _workerThread;
        private volatile bool _stopRequested;
        private volatile bool _isRunning;
        private volatile bool _isConnected;
        private Texture2D _texture;
        private byte[] _latestFrame = Array.Empty<byte>();
        private byte[] _applyFrame = Array.Empty<byte>();
        private float _effectiveInputFps = 60f;
        private int _latestFrameId;
        private int _appliedFrameId;
        private long _decodedFrames;
        private long _droppedFrames;
        private long _restartCount;
        private float _decodedFps;
        private float _fpsWindowStartTime = -1f;
        private long _fpsWindowStartDecodedFrames;
        private string _lastError = string.Empty;

        public event Action<Texture2D> FrameUpdated;

        public Texture2D CurrentTexture => _texture;
        public int OutputWidth => outputWidth;
        public int OutputHeight => outputHeight;
        public bool IsRunning => _isRunning;
        public bool IsConnected => _isConnected;
        public float DecodedFps => _decodedFps;
        public float EffectiveInputFps => _effectiveInputFps;
        public long DecodedFrames => Interlocked.Read(ref _decodedFrames);
        public long DroppedFrames => Interlocked.Read(ref _droppedFrames);
        public long RestartCount => Interlocked.Read(ref _restartCount);
        public string LastError
        {
            get
            {
                lock (_stateLock)
                {
                    return _lastError;
                }
            }
        }

        public string StreamUrl
        {
            get => rtspUrl;
            set => rtspUrl = value ?? string.Empty;
        }

        public string DisplayUrl => SanitizeRtspUrl(rtspUrl);

        public void SetStreamingActive(bool active)
        {
            if (active)
            {
                if (!enabled)
                {
                    enabled = true;
                }
                StartReceiver();
                return;
            }

            StopReceiver();
            if (enabled)
            {
                enabled = false;
            }
        }

        public bool ApplyOutputResolution(int width, int height, bool restartIfRunning)
        {
            if (!TryGetFrameBytes(width, height, out _))
            {
                SetLastError("[RtspBaselineReceiver] invalid output resolution.");
                return false;
            }

            bool isActive = _isRunning || _workerThread != null;
            if (isActive && !restartIfRunning)
            {
                SetLastError("[RtspBaselineReceiver] resolution change requires receiver restart.");
                return false;
            }

            bool changed = outputWidth != width || outputHeight != height;
            bool shouldRestart = changed && restartIfRunning && isActive;
            if (!changed)
            {
                return true;
            }

            if (shouldRestart)
            {
                if (!StopReceiver())
                {
                    return false;
                }
            }

            outputWidth = width;
            outputHeight = height;
            ClearBufferedFrameForResolutionChange();

            if (shouldRestart)
            {
                StartReceiver();
            }

            UnityEngine.Debug.Log(
                "[RtspBaselineReceiver] output resolution updated: "
                + outputWidth.ToString(CultureInfo.InvariantCulture)
                + "x"
                + outputHeight.ToString(CultureInfo.InvariantCulture)
                + (shouldRestart ? " (receiver restarted)" : string.Empty)
            );
            return true;
        }

        public bool ApplyStreamUrl(string newUrl, bool restartIfRunning)
        {
            string trimmed = (newUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                SetLastError("[RtspBaselineReceiver] empty RTSP URL.");
                return false;
            }

            bool shouldRestart = restartIfRunning && (_isRunning || _workerThread != null);
            if (shouldRestart)
            {
                if (!StopReceiver())
                {
                    return false;
                }
            }

            rtspUrl = trimmed;
            SetLastError(string.Empty);

            if (shouldRestart)
            {
                StartReceiver();
            }

            UnityEngine.Debug.Log(
                "[RtspBaselineReceiver] RTSP URL updated: "
                + DisplayUrl
                + (shouldRestart ? " (receiver restarted)" : string.Empty)
            );
            return true;
        }

        private void Awake()
        {
            ApplyRuntimeOverrides();
        }

        private void OnEnable()
        {
            if (autoStartOnEnable)
            {
                StartReceiver();
            }
        }

        private void OnDisable()
        {
            StopReceiver();
        }

        private void OnDestroy()
        {
            if (_texture != null)
            {
                Destroy(_texture);
                _texture = null;
            }
        }

        private void Update()
        {
            UpdateDecodedFps();
            ApplyLatestFrame();
        }

        public void StartReceiver()
        {
            if (_isRunning || _workerThread != null)
            {
                return;
            }

            if (outputWidth <= 0 || outputHeight <= 0)
            {
                SetLastError("[RtspBaselineReceiver] invalid output resolution.");
                return;
            }

            _stopRequested = false;
            _isConnected = false;
            _fpsWindowStartTime = -1f;
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "RtspBaselineReceiver"
            };
            _workerThread.Start();
        }

        public bool StopReceiver()
        {
            _stopRequested = true;
            StopFfmpegProcess();

            bool stopped = true;
            if (_workerThread != null)
            {
                stopped = _workerThread.Join(1000);
                if (!stopped)
                {
                    try
                    {
                        _workerThread.Interrupt();
                    }
                    catch (Exception)
                    {
                    }
                    stopped = _workerThread.Join(250);
                }
                if (stopped)
                {
                    _workerThread = null;
                }
            }

            if (stopped)
            {
                _isConnected = false;
                _isRunning = false;
            }
            else
            {
                SetLastError("[RtspBaselineReceiver] receiver thread did not stop in time.");
            }

            return stopped;
        }

        private void WorkerLoop()
        {
            _isRunning = true;
            if (!TryGetFrameBytes(outputWidth, outputHeight, out int frameBytes))
            {
                SetLastError("[RtspBaselineReceiver] invalid output resolution.");
                _isConnected = false;
                _isRunning = false;
                return;
            }
            byte[] readBuffer = new byte[frameBytes];
            byte[] candidateBuffer = new byte[frameBytes];
            float effectiveInputFps = ResolveInputFps();
            _effectiveInputFps = effectiveInputFps;

            while (!_stopRequested)
            {
                Process process = null;
                try
                {
                    process = StartFfmpegProcess();
                    if (process == null)
                    {
                        SleepReconnect();
                        continue;
                    }

                    Stream stream = process.StandardOutput.BaseStream;
                    while (!_stopRequested)
                    {
                        bool streamEnded = false;
                        if (!ReadExact(stream, readBuffer, frameBytes))
                        {
                            break;
                        }

                        int skippedFrames = 0;
                        while (!_stopRequested && HasCompleteBufferedFrame(
                                   TryGetPipeAvailableBytes(stream),
                                   frameBytes
                               ))
                        {
                            if (!ReadExact(stream, candidateBuffer, frameBytes))
                            {
                                streamEnded = true;
                                break;
                            }

                            byte[] swap = readBuffer;
                            readBuffer = candidateBuffer;
                            candidateBuffer = swap;
                            skippedFrames += 1;
                        }
                        if (streamEnded)
                        {
                            break;
                        }

                        lock (_stateLock)
                        {
                            if (_latestFrame.Length != frameBytes)
                            {
                                _latestFrame = new byte[frameBytes];
                            }
                            Buffer.BlockCopy(readBuffer, 0, _latestFrame, 0, frameBytes);
                            _latestFrameId += 1;
                        }

                        Interlocked.Increment(ref _decodedFrames);
                        if (skippedFrames > 0)
                        {
                            Interlocked.Add(ref _droppedFrames, skippedFrames);
                        }
                        _isConnected = true;
                    }
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SetLastError("[RtspBaselineReceiver] decode loop failed: " + ex.Message);
                }
                finally
                {
                    _isConnected = false;
                    StopFfmpegProcess();
                }

                if (_stopRequested)
                {
                    break;
                }

                Interlocked.Increment(ref _restartCount);
                SleepReconnect();
            }

            _isConnected = false;
            _isRunning = false;
        }

        private void ClearBufferedFrameForResolutionChange()
        {
            lock (_stateLock)
            {
                _latestFrame = Array.Empty<byte>();
                _applyFrame = Array.Empty<byte>();
                _latestFrameId = 0;
                _appliedFrameId = 0;
                _decodedFps = 0f;
                _fpsWindowStartTime = -1f;
                _fpsWindowStartDecodedFrames = Interlocked.Read(ref _decodedFrames);
            }

            if (_texture != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_texture);
                }
                else
                {
                    DestroyImmediate(_texture);
                }
                _texture = null;
            }
        }

        private bool TryGetFrameBytes(int width, int height, out int frameBytes)
        {
            frameBytes = 0;
            if (width < 16 || height < 16)
            {
                return false;
            }

            long requiredBytes = (long)width * (long)height * 3L;
            if (requiredBytes <= 0L || requiredBytes > maxFrameBytes || requiredBytes > int.MaxValue)
            {
                return false;
            }

            frameBytes = (int)requiredBytes;
            return true;
        }

        private Process StartFfmpegProcess()
        {
            if (string.IsNullOrWhiteSpace(rtspUrl))
            {
                SetLastError("[RtspBaselineReceiver] empty RTSP URL.");
                return null;
            }

            string resolvedFfmpegExecutable = ResolveFfmpegExecutable();
            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedFfmpegExecutable,
                Arguments = BuildFfmpegArguments(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                var process = new Process { StartInfo = startInfo, EnableRaisingEvents = false };
                process.ErrorDataReceived += OnFfmpegErrorData;
                process.Start();
                process.BeginErrorReadLine();

                lock (_processLock)
                {
                    _ffmpegProcess = process;
                }

                SetLastError(string.Empty);
                return process;
            }
            catch (Exception ex)
            {
                SetLastError("[RtspBaselineReceiver] ffmpeg start failed: " + ex.Message);
                return null;
            }
        }

        private float ResolveInputFps()
        {
            float fallback = Mathf.Max(1f, fallbackInputFps);
            if (!autoDetectInputFps)
            {
                return fallback;
            }

            string resolvedFfprobeExecutable = ResolveFfprobeExecutable();
            if (string.IsNullOrWhiteSpace(resolvedFfprobeExecutable))
            {
                return fallback;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedFfprobeExecutable,
                Arguments = BuildFfprobeArguments(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return fallback;
                    }

                    if (!process.WaitForExit(Mathf.Max(100, ffprobeTimeoutMs)))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                        }
                        return fallback;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    if (process.ExitCode == 0 && TryParseFfprobeFrameRate(output, out float detectedFps))
                    {
                        return detectedFps;
                    }
                }
            }
            catch (Exception ex)
            {
                if (verboseFfmpegLog)
                {
                    UnityEngine.Debug.Log("[RtspBaselineReceiver] ffprobe fps detection failed: " + ex.Message);
                }
            }

            return fallback;
        }

        private string BuildFfprobeArguments()
        {
            string transport = preferTcpTransport ? "tcp" : "udp";
            var sb = new StringBuilder();
            sb.Append("-v error ");
            sb.Append("-select_streams v:0 ");
            if (enableLowLatencyInputOptions)
            {
                if (probeSizeBytes > 0)
                {
                    sb.Append("-probesize ")
                        .Append(probeSizeBytes.ToString(CultureInfo.InvariantCulture))
                        .Append(' ');
                }
                sb.Append("-analyzeduration ")
                    .Append(analyzeDurationUs.ToString(CultureInfo.InvariantCulture))
                    .Append(' ');
                sb.Append("-max_delay ")
                    .Append(maxDelayUs.ToString(CultureInfo.InvariantCulture))
                    .Append(' ');
            }
            sb.Append("-rtsp_transport ").Append(transport).Append(' ');
            sb.Append("-show_entries stream=avg_frame_rate,r_frame_rate ");
            sb.Append("-of default=noprint_wrappers=1:nokey=1 ");
            sb.Append('"').Append(EscapeForQuotes(rtspUrl)).Append('"');
            return sb.ToString();
        }

        private static bool TryParseFfprobeFrameRate(string output, out float frameRate)
        {
            frameRate = 0f;
            if (string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)
                    || line.Equals("N/A", StringComparison.OrdinalIgnoreCase)
                    || line == "0/0")
                {
                    continue;
                }

                float parsedRate;
                int slashIndex = line.IndexOf('/');
                if (slashIndex > 0)
                {
                    string numeratorText = line.Substring(0, slashIndex);
                    string denominatorText = line.Substring(slashIndex + 1);
                    if (!float.TryParse(numeratorText, NumberStyles.Float, CultureInfo.InvariantCulture, out float numerator)
                        || !float.TryParse(denominatorText, NumberStyles.Float, CultureInfo.InvariantCulture, out float denominator)
                        || denominator <= 0f)
                    {
                        continue;
                    }
                    parsedRate = numerator / denominator;
                }
                else if (!float.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedRate))
                {
                    continue;
                }

                if (parsedRate >= 1f && parsedRate <= 1000f)
                {
                    frameRate = parsedRate;
                    return true;
                }
            }

            return false;
        }

        private void StopFfmpegProcess()
        {
            lock (_processLock)
            {
                if (_ffmpegProcess == null)
                {
                    return;
                }

                try
                {
                    if (!_ffmpegProcess.HasExited)
                    {
                        _ffmpegProcess.Kill();
                    }
                }
                catch (Exception)
                {
                }

                try
                {
                    _ffmpegProcess.Dispose();
                }
                catch (Exception)
                {
                }

                _ffmpegProcess = null;
            }
        }

        private void OnFfmpegErrorData(object sender, DataReceivedEventArgs args)
        {
            if (args == null || string.IsNullOrWhiteSpace(args.Data))
            {
                return;
            }

            string line = args.Data.Trim();
            if (verboseFfmpegLog)
            {
                UnityEngine.Debug.Log("[RtspBaselineReceiver] " + line);
            }
            else if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                     || line.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0
                     || line.IndexOf("unable", StringComparison.OrdinalIgnoreCase) >= 0
                     || line.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0
                     || line.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                     || line.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                     || line.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetLastError("[RtspBaselineReceiver] " + line);
            }
        }

        private void ApplyLatestFrame()
        {
            if (!TryCopyLatestFrameForApply(out byte[] frameData, out _, out int dropped))
            {
                return;
            }

            if (_texture == null || _texture.width != outputWidth || _texture.height != outputHeight)
            {
                if (_texture != null)
                {
                    Destroy(_texture);
                }
                _texture = new Texture2D(outputWidth, outputHeight, TextureFormat.RGB24, false, false);
                _texture.wrapMode = TextureWrapMode.Clamp;
                _texture.filterMode = FilterMode.Bilinear;
            }

            _texture.LoadRawTextureData(frameData);
            _texture.Apply(false, false);

            if (dropped > 0)
            {
                Interlocked.Add(ref _droppedFrames, dropped);
            }

            if (FrameUpdated != null)
            {
                FrameUpdated.Invoke(_texture);
            }
        }

        private bool TryCopyLatestFrameForApply(out byte[] frameData, out int frameId, out int dropped)
        {
            frameData = Array.Empty<byte>();
            frameId = 0;
            dropped = 0;

            lock (_stateLock)
            {
                if (_latestFrameId == _appliedFrameId || _latestFrame.Length == 0)
                {
                    return false;
                }

                if (_latestFrameId > _appliedFrameId + 1)
                {
                    dropped = _latestFrameId - _appliedFrameId - 1;
                }

                if (_applyFrame.Length != _latestFrame.Length)
                {
                    _applyFrame = new byte[_latestFrame.Length];
                }
                Buffer.BlockCopy(_latestFrame, 0, _applyFrame, 0, _latestFrame.Length);
                frameData = _applyFrame;
                frameId = _latestFrameId;
                _appliedFrameId = _latestFrameId;
            }

            return true;
        }

        private void UpdateDecodedFps()
        {
            float now = Time.unscaledTime;
            long decodedNow = Interlocked.Read(ref _decodedFrames);

            if (_fpsWindowStartTime < 0f)
            {
                _fpsWindowStartTime = now;
                _fpsWindowStartDecodedFrames = decodedNow;
                _decodedFps = 0f;
                return;
            }

            float elapsed = now - _fpsWindowStartTime;
            if (elapsed < 1.0f)
            {
                return;
            }

            long frameDelta = decodedNow - _fpsWindowStartDecodedFrames;
            _decodedFps = frameDelta / Mathf.Max(0.001f, elapsed);
            _fpsWindowStartTime = now;
            _fpsWindowStartDecodedFrames = decodedNow;
        }

        private string BuildFfmpegArguments()
        {
            string transport = preferTcpTransport ? "tcp" : "udp";
            string scale = "scale=" + outputWidth.ToString(CultureInfo.InvariantCulture) + ":"
                + outputHeight.ToString(CultureInfo.InvariantCulture) + ":flags=bicubic";
            string videoFilter = maxDecodeFps > 0f
                ? "fps=" + maxDecodeFps.ToString("F3", CultureInfo.InvariantCulture) + "," + scale
                : scale;

            var sb = new StringBuilder();
            sb.Append("-hide_banner ");
            sb.Append("-loglevel ");
            sb.Append(verboseFfmpegLog ? "info " : "warning ");
            sb.Append("-fflags nobuffer -flags low_delay ");
            if (enableLowLatencyInputOptions)
            {
                if (useDirectIo)
                {
                    sb.Append("-avioflags direct ");
                }
                if (probeSizeBytes > 0)
                {
                    sb.Append("-probesize ")
                        .Append(probeSizeBytes.ToString(CultureInfo.InvariantCulture))
                        .Append(' ');
                }
                sb.Append("-analyzeduration ")
                    .Append(analyzeDurationUs.ToString(CultureInfo.InvariantCulture))
                    .Append(' ');
                sb.Append("-max_delay ")
                    .Append(maxDelayUs.ToString(CultureInfo.InvariantCulture))
                    .Append(' ');
                sb.Append("-reorder_queue_size ")
                    .Append(reorderQueueSize.ToString(CultureInfo.InvariantCulture))
                    .Append(' ');
            }
            sb.Append("-rtsp_transport ").Append(transport).Append(' ');
            sb.Append("-i ").Append('"').Append(EscapeForQuotes(rtspUrl)).Append("\" ");
            sb.Append("-an -sn -dn ");
            sb.Append("-vf ").Append('"').Append(videoFilter).Append("\" ");
            sb.Append("-pix_fmt rgb24 -f rawvideo pipe:1");
            return sb.ToString();
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int requiredBytes)
        {
            int offset = 0;
            while (offset < requiredBytes)
            {
                int bytesRead = 0;
                try
                {
                    bytesRead = stream.Read(buffer, offset, requiredBytes - offset);
                }
                catch (IOException)
                {
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }

                if (bytesRead <= 0)
                {
                    return false;
                }
                offset += bytesRead;
            }

            return true;
        }

        private static bool HasCompleteBufferedFrame(int availableBytes, int frameBytes)
        {
            return frameBytes > 0 && availableBytes >= frameBytes;
        }

        private static int TryGetPipeAvailableBytes(Stream stream)
        {
            if (stream == null)
            {
                return PipePeekUnavailableBytes;
            }

            try
            {
                if (stream is FileStream fileStream)
                {
                    IntPtr handle = fileStream.SafeFileHandle.DangerousGetHandle();
                    if (handle != IntPtr.Zero
                        && PeekNamedPipe(
                            handle,
                            IntPtr.Zero,
                            0,
                            IntPtr.Zero,
                            out uint availableBytes,
                            IntPtr.Zero
                        ))
                    {
                        return availableBytes > int.MaxValue ? int.MaxValue : (int)availableBytes;
                    }
                }
            }
            catch (Exception)
            {
            }

            return PipePeekUnavailableBytes;
        }

        private void SleepReconnect()
        {
            if (reconnectDelayMs <= 0)
            {
                return;
            }

            int remaining = reconnectDelayMs;
            const int chunkMs = 100;
            while (!_stopRequested && remaining > 0)
            {
                int sleepMs = Math.Min(chunkMs, remaining);
                Thread.Sleep(sleepMs);
                remaining -= sleepMs;
            }
        }

        private void ApplyRuntimeOverrides()
        {
            if (!allowRuntimeOverrides)
            {
                return;
            }

            string runtimeRtspUrl = ResolveRuntimeOverride(rtspUrlEnvName, rtspUrlArgName);
            if (!string.IsNullOrWhiteSpace(runtimeRtspUrl))
            {
                rtspUrl = runtimeRtspUrl.Trim();
                UnityEngine.Debug.Log(
                    "[RtspBaselineReceiver] RTSP URL override applied: " + DisplayUrl
                );
            }

            string runtimeFfmpegExe = ResolveRuntimeOverride(
                ffmpegExecutableEnvName,
                ffmpegExecutableArgName
            );
            if (!string.IsNullOrWhiteSpace(runtimeFfmpegExe))
            {
                ffmpegExecutable = runtimeFfmpegExe.Trim();
                UnityEngine.Debug.Log(
                    "[RtspBaselineReceiver] ffmpeg executable override applied: " + ffmpegExecutable
                );
            }

            string runtimeFfprobeExe = ResolveRuntimeOverride(
                ffprobeExecutableEnvName,
                ffprobeExecutableArgName
            );
            if (!string.IsNullOrWhiteSpace(runtimeFfprobeExe))
            {
                ffprobeExecutable = runtimeFfprobeExe.Trim();
                UnityEngine.Debug.Log(
                    "[RtspBaselineReceiver] ffprobe executable override applied: " + ffprobeExecutable
                );
            }
        }

        private static string ResolveRuntimeOverride(string envName, string argName)
        {
            if (!string.IsNullOrWhiteSpace(envName))
            {
                string envValue = Environment.GetEnvironmentVariable(envName);
                if (!string.IsNullOrWhiteSpace(envValue))
                {
                    return envValue;
                }
            }

            if (string.IsNullOrWhiteSpace(argName))
            {
                return string.Empty;
            }

            string[] args = Environment.GetCommandLineArgs();
            for (int index = 0; index < args.Length; index += 1)
            {
                string arg = args[index];
                if (arg.Equals(argName, StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 < args.Length)
                    {
                        return args[index + 1];
                    }
                    continue;
                }

                string prefix = argName + "=";
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(prefix.Length);
                }
            }

            return string.Empty;
        }

        private string ResolveFfmpegExecutable()
        {
            string configured = (ffmpegExecutable ?? string.Empty).Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (Path.IsPathRooted(configured) || File.Exists(configured))
                {
                    return configured;
                }

                string nearby = TryFindNearbyFfmpegExecutable();
                if (!string.IsNullOrWhiteSpace(nearby))
                {
                    return nearby;
                }

                return configured;
            }

            string fallback = TryFindNearbyFfmpegExecutable();
            return string.IsNullOrWhiteSpace(fallback) ? "ffmpeg" : fallback;
        }

        private string ResolveFfprobeExecutable()
        {
            string configured = (ffprobeExecutable ?? string.Empty).Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (Path.IsPathRooted(configured) || File.Exists(configured))
                {
                    return configured;
                }

                string nearby = TryFindNearbyFfprobeExecutable();
                if (!string.IsNullOrWhiteSpace(nearby))
                {
                    return nearby;
                }

                return configured;
            }

            string ffmpegPath = ResolveFfmpegExecutable();
            if (!string.IsNullOrWhiteSpace(ffmpegPath)
                && !ffmpegPath.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase))
            {
                string sibling = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? string.Empty, "ffprobe.exe");
                if (File.Exists(sibling))
                {
                    return sibling;
                }
            }

            string fallback = TryFindNearbyFfprobeExecutable();
            return string.IsNullOrWhiteSpace(fallback) ? "ffprobe" : fallback;
        }

        private static string TryFindNearbyFfmpegExecutable()
        {
            string dataPath = Application.dataPath;
            string[] candidates =
            {
                Path.GetFullPath(Path.Combine(dataPath, "..", "..", "ffmpeg", "bin", "ffmpeg.exe")),
                Path.GetFullPath(Path.Combine(dataPath, "..", "ffmpeg", "bin", "ffmpeg.exe")),
                Path.GetFullPath(Path.Combine(dataPath, "..", "Tools", "ffmpeg", "bin", "ffmpeg.exe")),
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static string TryFindNearbyFfprobeExecutable()
        {
            string dataPath = Application.dataPath;
            string[] candidates =
            {
                Path.GetFullPath(Path.Combine(dataPath, "..", "..", "ffmpeg", "bin", "ffprobe.exe")),
                Path.GetFullPath(Path.Combine(dataPath, "..", "ffmpeg", "bin", "ffprobe.exe")),
                Path.GetFullPath(Path.Combine(dataPath, "..", "Tools", "ffmpeg", "bin", "ffprobe.exe")),
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private void SetLastError(string message)
        {
            lock (_stateLock)
            {
                _lastError = message ?? string.Empty;
            }
        }

        private static string EscapeForQuotes(string value)
        {
            return (value ?? string.Empty).Replace("\"", "\\\"");
        }

        private static string SanitizeRtspUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri parsed))
            {
                return value;
            }

            if (string.IsNullOrEmpty(parsed.UserInfo))
            {
                return value;
            }

            var builder = new UriBuilder(parsed)
            {
                UserName = string.Empty,
                Password = string.Empty
            };
            return builder.Uri.ToString();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PeekNamedPipe(
            IntPtr hNamedPipe,
            IntPtr lpBuffer,
            uint nBufferSize,
            IntPtr lpBytesRead,
            out uint lpTotalBytesAvail,
            IntPtr lpBytesLeftThisMessage
        );
    }
}





