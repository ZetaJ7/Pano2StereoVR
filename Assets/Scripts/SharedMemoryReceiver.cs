using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using UnityEngine;

namespace Pano2StereoVR
{
    public unsafe sealed class SharedMemoryReceiver : MonoBehaviour
    {
        private const int SeqBeginOffset = 0;
        private const int WidthOffset = 8;
        private const int HeightOffset = 12;
        private const int ModeOffset = 16;
        private const int SeqEndOffset = 20;
        private const int HeaderBytes = 28;

        [SerializeField] private string shmName = "pano2stereo";
        [SerializeField] private bool autoRead = true;
        [SerializeField] [Range(1, 16)] private int readAttemptsPerUpdate = 4;
        [SerializeField] [Min(1048576)] private int maxFrameBytes = 134217728;
        [SerializeField] [Range(1, 120)] private int reopenIntervalFrames = 10;

        private MemoryMappedFile _mappedFile;
        private MemoryMappedViewAccessor _accessor;
        private SafeMemoryMappedViewHandle _viewHandle;
        private byte* _shmPtr;
        private bool _pointerAcquired;
        private Texture2D _stereoTexture;
        private byte[] _pixelBuffer = Array.Empty<byte>();
        private ulong _lastSeq;
        private int _framesSinceOpenAttempt;
        private float _writerFpsWindowStartTime = -1f;
        private ulong _writerFpsWindowStartSeq;

        public event Action<Texture2D, int> FrameUpdated;
        public event Action<int, ulong, float> ModeApplied;

        public Texture2D StereoTexture => _stereoTexture;
        public int CurrentMode { get; private set; } = 3;
        public float LastModeAppliedTime { get; private set; } = -1f;
        public int LastAppliedMode { get; private set; } = 3;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public long AcceptedFrames { get; private set; }
        public long ModeChangesApplied { get; private set; }
        public long WriterBusySkips { get; private set; }
        public long TornRejected { get; private set; }
        public bool IsOpened => _accessor != null;
        public float ObservedWriterFps { get; private set; }
        public ulong LastObservedSeq { get; private set; }

        private void OnEnable()
        {
            TryOpen();
        }

        private void Update()
        {
            if (!autoRead)
            {
                return;
            }
            TryReadLatestFrame();
        }

        private void OnDisable()
        {
            Close();
        }

        public bool TryReadLatestFrame()
        {
            if (_accessor == null)
            {
                _framesSinceOpenAttempt += 1;
                if (_framesSinceOpenAttempt < reopenIntervalFrames)
                {
                    return false;
                }
                _framesSinceOpenAttempt = 0;
                if (!TryOpen())
                {
                    return false;
                }
            }

            try
            {
                for (int attempt = 0; attempt < readAttemptsPerUpdate; attempt++)
                {
                    if (!_pointerAcquired || _shmPtr == null)
                    {
                        return false;
                    }

                    ulong seqBegin = ReadUInt64(_shmPtr, SeqBeginOffset);
                    if ((seqBegin & 1UL) == 1UL)
                    {
                        WriterBusySkips += 1;
                        continue;
                    }
                    UpdateObservedWriterFps(seqBegin);
                    if (seqBegin == _lastSeq)
                    {
                        return false;
                    }

                    ulong seqEndSnapshot = ReadUInt64(_shmPtr, SeqEndOffset);
                    if (seqEndSnapshot != seqBegin || (seqEndSnapshot & 1UL) == 1UL)
                    {
                        TornRejected += 1;
                        continue;
                    }

                    uint width = ReadUInt32(_shmPtr, WidthOffset);
                    uint height = ReadUInt32(_shmPtr, HeightOffset);
                    uint mode = ReadUInt32(_shmPtr, ModeOffset);

                    long requiredBytesLong = (long)width * (long)height * 3L;
                    if (width == 0 || height == 0 || requiredBytesLong <= 0)
                    {
                        continue;
                    }
                    if (requiredBytesLong > maxFrameBytes || requiredBytesLong > int.MaxValue)
                    {
                        continue;
                    }

                    int requiredBytes = (int)requiredBytesLong;
                    if (_pixelBuffer.Length != requiredBytes)
                    {
                        _pixelBuffer = new byte[requiredBytes];
                    }

                    EnsureTexture((int)width, (int)height);
                    Marshal.Copy((IntPtr)(_shmPtr + HeaderBytes), _pixelBuffer, 0, requiredBytes);

                    ulong seqEnd = ReadUInt64(_shmPtr, SeqEndOffset);
                    ulong seqBeginAfter = ReadUInt64(_shmPtr, SeqBeginOffset);
                    if (seqEnd != seqBegin || seqBeginAfter != seqBegin || (seqEnd & 1UL) == 1UL)
                    {
                        TornRejected += 1;
                        break;
                    }

                    _stereoTexture.LoadRawTextureData(_pixelBuffer);
                    _stereoTexture.Apply(false, false);

                    _lastSeq = seqBegin;
                    int previousMode = CurrentMode;
                    CurrentMode = (int)mode;
                    AcceptedFrames += 1;

                    if (AcceptedFrames == 1 || CurrentMode != previousMode)
                    {
                        LastAppliedMode = CurrentMode;
                        LastModeAppliedTime = Time.unscaledTime;
                        if (AcceptedFrames > 1)
                        {
                            ModeChangesApplied += 1;
                        }
                        ModeApplied?.Invoke(CurrentMode, seqBegin, LastModeAppliedTime);
                    }

                    FrameUpdated?.Invoke(_stereoTexture, CurrentMode);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SharedMemoryReceiver] read failed: " + ex.Message);
                Close();
                return false;
            }
        }

        private void UpdateObservedWriterFps(ulong seqBegin)
        {
            if (seqBegin == 0 || (seqBegin & 1UL) == 1UL)
            {
                return;
            }

            LastObservedSeq = seqBegin;
            if (_writerFpsWindowStartTime < 0f)
            {
                _writerFpsWindowStartTime = Time.unscaledTime;
                _writerFpsWindowStartSeq = seqBegin;
                return;
            }

            float elapsed = Time.unscaledTime - _writerFpsWindowStartTime;
            if (elapsed < 1.0f)
            {
                return;
            }

            ulong seqDelta = seqBegin >= _writerFpsWindowStartSeq
                ? (seqBegin - _writerFpsWindowStartSeq)
                : 0UL;
            float frameDelta = seqDelta / 2f;
            ObservedWriterFps = frameDelta / Mathf.Max(0.001f, elapsed);

            _writerFpsWindowStartTime = Time.unscaledTime;
            _writerFpsWindowStartSeq = seqBegin;
        }

        public bool TryOpen()
        {
            if (_accessor != null)
            {
                return true;
            }

            try
            {
                _mappedFile = MemoryMappedFile.OpenExisting(shmName, MemoryMappedFileRights.Read);
                _accessor = _mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                _viewHandle = _accessor.SafeMemoryMappedViewHandle;

                byte* shmPtr = null;
                _viewHandle.AcquirePointer(ref shmPtr);
                if (shmPtr == null)
                {
                    throw new InvalidOperationException("Failed to acquire shared memory pointer.");
                }

                _shmPtr = shmPtr;
                _pointerAcquired = true;
                _lastSeq = 0;
                _framesSinceOpenAttempt = 0;
                return true;
            }
            catch
            {
                Close();
                return false;
            }
        }

        public void Close()
        {
            if (_pointerAcquired && _viewHandle != null)
            {
                _viewHandle.ReleasePointer();
                _pointerAcquired = false;
            }
            _shmPtr = null;
            _viewHandle = null;

            if (_accessor != null)
            {
                _accessor.Dispose();
                _accessor = null;
            }
            if (_mappedFile != null)
            {
                _mappedFile.Dispose();
                _mappedFile = null;
            }
        }

        private void EnsureTexture(int width, int height)
        {
            if (_stereoTexture != null && Width == width && Height == height)
            {
                return;
            }

            Width = width;
            Height = height;
            if (_stereoTexture != null)
            {
                Destroy(_stereoTexture);
            }
            _stereoTexture = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            _stereoTexture.wrapMode = TextureWrapMode.Clamp;
            _stereoTexture.filterMode = FilterMode.Bilinear;
        }

        private static uint ReadUInt32(byte* basePtr, int offset)
        {
            return *(uint*)(basePtr + offset);
        }

        private static ulong ReadUInt64(byte* basePtr, int offset)
        {
            return *(ulong*)(basePtr + offset);
        }
    }
}
