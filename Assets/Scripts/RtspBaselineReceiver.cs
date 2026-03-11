using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Pano2StereoVR
{
    public sealed class RtspBaselineReceiver : MonoBehaviour
    {
        [SerializeField] private string ffmpegExecutable = "ffmpeg";
        [SerializeField] private string rtspUrl = string.Empty;
        [SerializeField] [Min(16)] private int outputWidth = 1920;
        [SerializeField] [Min(16)] private int outputHeight = 1080;
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
        [SerializeField] private bool verboseFfmpegLog = false;
        [SerializeField] private bool allowRuntimeOverrides = true;
        [SerializeField] private string rtspUrlArgName = "--rtsp-url";
        [SerializeField] private string ffmpegExecutableArgName = "--ffmpeg-exe";
        [SerializeField] private string rtspUrlEnvName = "P2SVR_RTSP_URL";
        [SerializeField] private string ffmpegExecutableEnvName = "P2SVR_FFMPEG_EXE";

        private readonly object _stateLock = new object();
        private readonly object _processLock = new object();
        private Process _ffmpegProcess;
        private Thread _workerThread;
        private volatile bool _stopRequested;
        private volatile bool _isRunning;
        private volatile bool _isConnected;
        private Texture2D _texture;
        private byte[] _latestFrame = Array.Empty<byte>();
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

        public bool ApplyStreamUrl(string newUrl, bool restartIfRunning)
        {
            string trimmed = (newUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                SetLastError("[RtspBaselineReceiver] empty RTSP URL.");
                return false;
            }

            bool shouldRestart = restartIfRunning && (_isRunning || _workerThread != null);
            rtspUrl = trimmed;
            SetLastError(string.Empty);

            if (shouldRestart)
            {
                StopReceiver();
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

        public void StopReceiver()
        {
            _stopRequested = true;
            StopFfmpegProcess();

            if (_workerThread != null)
            {
                if (!_workerThread.Join(1000))
                {
                    try
                    {
                        _workerThread.Interrupt();
                    }
                    catch (Exception)
                    {
                    }
                }
                _workerThread = null;
            }

            _isConnected = false;
            _isRunning = false;
        }

        private void WorkerLoop()
        {
            _isRunning = true;
            int frameBytes = outputWidth * outputHeight * 3;
            byte[] readBuffer = new byte[frameBytes];

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
                        if (!ReadExact(stream, readBuffer, frameBytes))
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
            bool hasFrame = false;
            int dropped = 0;

            lock (_stateLock)
            {
                if (_latestFrameId == _appliedFrameId || _latestFrame.Length == 0)
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

                if (_latestFrameId > _appliedFrameId + 1)
                {
                    dropped = _latestFrameId - _appliedFrameId - 1;
                }

                _texture.LoadRawTextureData(_latestFrame);
                _texture.Apply(false, false);

                _appliedFrameId = _latestFrameId;
                hasFrame = true;
            }

            if (dropped > 0)
            {
                Interlocked.Add(ref _droppedFrames, dropped);
            }

            if (!hasFrame)
            {
                return;
            }

            if (FrameUpdated != null)
            {
                FrameUpdated.Invoke(_texture);
            }
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
    }
}





