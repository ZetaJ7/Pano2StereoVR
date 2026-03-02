using System;
using System.IO.MemoryMappedFiles;
using UnityEngine;

namespace Pano2StereoVR
{
    public sealed class SharedMemoryReceiver : MonoBehaviour
    {
        [SerializeField] private string shmName = "pano2stereo";
        [SerializeField] private bool autoRead = true;
        [SerializeField] [Min(1048576)] private int maxFrameBytes = 134217728;
        [SerializeField] [Range(1, 120)] private int reopenIntervalFrames = 10;

        private MemoryMappedFile _mappedFile;
        private MemoryMappedViewAccessor _accessor;
        private Texture2D _stereoTexture;
        private byte[] _pixelBuffer = Array.Empty<byte>();
        private ulong _lastSeq;
        private int _framesSinceOpenAttempt;

        public event Action<Texture2D, int> FrameUpdated;

        public Texture2D StereoTexture => _stereoTexture;
        public int CurrentMode { get; private set; } = 3;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public long AcceptedFrames { get; private set; }
        public long WriterBusySkips { get; private set; }
        public long TornRejected { get; private set; }
        public bool IsOpened => _accessor != null;

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
                ulong seqBegin = _accessor.ReadUInt64(0);
                if ((seqBegin & 1UL) == 1UL)
                {
                    WriterBusySkips += 1;
                    return false;
                }

                uint width = _accessor.ReadUInt32(8);
                uint height = _accessor.ReadUInt32(12);
                uint mode = _accessor.ReadUInt32(16);

                long requiredBytesLong = (long)width * (long)height * 3L;
                if (width == 0 || height == 0 || requiredBytesLong <= 0)
                {
                    return false;
                }
                if (requiredBytesLong > maxFrameBytes || requiredBytesLong > int.MaxValue)
                {
                    return false;
                }

                int requiredBytes = (int)requiredBytesLong;
                if (_pixelBuffer.Length != requiredBytes)
                {
                    _pixelBuffer = new byte[requiredBytes];
                }

                EnsureTexture((int)width, (int)height);
                _accessor.ReadArray(28, _pixelBuffer, 0, requiredBytes);

                ulong seqEnd = _accessor.ReadUInt64(20);
                if (seqEnd != seqBegin || (seqEnd & 1UL) == 1UL)
                {
                    TornRejected += 1;
                    return false;
                }

                if (seqBegin == _lastSeq)
                {
                    return false;
                }

                _stereoTexture.LoadRawTextureData(_pixelBuffer);
                _stereoTexture.Apply(false, false);

                _lastSeq = seqBegin;
                CurrentMode = (int)mode;
                AcceptedFrames += 1;
                FrameUpdated?.Invoke(_stereoTexture, CurrentMode);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SharedMemoryReceiver] read failed: " + ex.Message);
                Close();
                return false;
            }
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
                _lastSeq = 0;
                _framesSinceOpenAttempt = 0;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Close()
        {
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
    }
}
