using System;
using System.Runtime.InteropServices;
using UnityEngine;

public enum DiscColor
{
    None = 0,
    Black = 1,
    White = 2
}

/// <summary>
/// XREAL (Android ARM64) 実機向けの超高速 C++ OpenCV 盤面認識ブリッジ
/// ゼロ GC Alloc、毎フレームのメモリ確保なし
/// </summary>
public class NativeOthelloRecognizer : IDisposable
{
    static NativeOthelloRecognizer()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var systemClass = new AndroidJavaClass("java.lang.System"))
            {
                systemClass.CallStatic("loadLibrary", "OthelloCvPlugin");
            }
            UnityEngine.Debug.Log("[NativeOthelloRecognizer] System.loadLibrary('OthelloCvPlugin') succeeded.");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[NativeOthelloRecognizer] System.loadLibrary note: {ex.Message}");
        }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private const string PLUGIN_NAME = "OthelloCvPlugin";

    [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int OthelloCv_RecognizeBoard(
        IntPtr srcPixels,
        int width,
        int height,
        int isBgra,
        [Out] int[] outBoard,
        [Out] float[] outCorners,
        IntPtr outDebugPixels,
        int warpSize,
        int minAreaThreshold,
        int blackThreshold,
        int whiteThreshold,
        int transparentBg
    );

    [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int OthelloCv_RecognizeBoardYUV(
        IntPtr yPlane,
        IntPtr uPlane,
        IntPtr vPlane,
        int width,
        int height,
        [Out] int[] outBoard,
        [Out] float[] outCorners,
        IntPtr outDebugPixels,
        int outWidth,
        int outHeight,
        int warpSize,
        int minAreaThreshold,
        int blackThreshold,
        int whiteThreshold,
        int transparentBg
    );
#endif

    // 再利用可能な固定長バッファ (GC Alloc ゼロ)
    private readonly int[] _boardBuffer = new int[64];
    private readonly float[] _cornerBuffer = new float[8];
    private byte[] _yBuffer;
    private byte[] _uBuffer;
    private byte[] _vBuffer;
    private Color32[] _yuvDebugPixels;
    private byte[] _nativeDebugByteBuffer;
    private GCHandle _nativeDebugHandle;

    public int WarpSize { get; set; } = 400;
    public int MinAreaThreshold { get; set; } = 5000;
    public int BlackThreshold { get; set; } = 65;
    public int WhiteThreshold { get; set; } = 165;
    public bool TransparentBackground { get; set; } = true;

    public bool TryRecognizeRaw(
        IntPtr pixelPtr,
        int width,
        int height,
        bool isBgra,
        DiscColor[,] outBoard,
        Vector2[] outCorners = null,
        IntPtr debugPixelPtr = default
    ) {
        if (pixelPtr == IntPtr.Zero || width <= 0 || height <= 0 || outBoard == null)
        {
            return false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        int result = OthelloCv_RecognizeBoard(
            pixelPtr,
            width,
            height,
            isBgra ? 1 : 0,
            _boardBuffer,
            _cornerBuffer,
            debugPixelPtr,
            WarpSize,
            MinAreaThreshold,
            BlackThreshold,
            WhiteThreshold,
            TransparentBackground ? 1 : 0
        );

        if (result == 1)
        {
            for (int y = 0; y < 8; ++y)
            {
                for (int x = 0; x < 8; ++x)
                {
                    outBoard[y, x] = (DiscColor)_boardBuffer[y * 8 + x];
                }
            }

            if (outCorners != null && outCorners.Length >= 4)
            {
                for (int i = 0; i < 4; ++i)
                {
                    outCorners[i] = new Vector2(_cornerBuffer[i * 2], _cornerBuffer[i * 2 + 1]);
                }
            }

            return true;
        }

        return false;
#else
        // Editor / PC 環境用シミュレーションフォールバック
        for (int y = 0; y < 8; ++y)
        {
            for (int x = 0; x < 8; ++x)
            {
                outBoard[y, x] = DiscColor.None;
            }
        }
        // 初期配置 + サンプル配置
        outBoard[3, 3] = DiscColor.White;
        outBoard[4, 4] = DiscColor.White;
        outBoard[3, 4] = DiscColor.Black;
        outBoard[4, 3] = DiscColor.Black;
        outBoard[2, 3] = DiscColor.Black;
        outBoard[5, 4] = DiscColor.White;

        if (outCorners != null && outCorners.Length >= 4)
        {
            float marginX = width * 0.15f;
            float marginY = height * 0.15f;
            outCorners[0] = new Vector2(marginX, marginY);
            outCorners[1] = new Vector2(width - marginX, marginY);
            outCorners[2] = new Vector2(width - marginX, height - marginY);
            outCorners[3] = new Vector2(marginX, height - marginY);
        }

        if (debugPixelPtr != IntPtr.Zero)
        {
            int totalBytes = width * height * 4;
            byte[] tempBuf = new byte[totalBytes];
            Marshal.Copy(pixelPtr, tempBuf, 0, totalBytes);
            Marshal.Copy(tempBuf, 0, debugPixelPtr, totalBytes);
        }

        return true;
#endif
    }

    /// <summary>
    /// Texture2D からの盤面認識 (デバッグ画像バッファ出力対応)
    /// </summary>
    public bool TryRecognizeTexture(
        Texture2D texture,
        DiscColor[,] outBoard,
        Vector2[] outCorners = null,
        Texture2D debugTexture = null
    ) {
        if (texture == null || outBoard == null) return false;

        int width = texture.width;
        int height = texture.height;
        Color32[] pixels = texture.GetPixels32();
        GCHandle srcHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);

        Color32[] debugPixels = null;
        GCHandle debugHandle = default;
        bool hasDebug = debugTexture != null;

        try
        {
            IntPtr srcPtr = srcHandle.AddrOfPinnedObject();
            IntPtr debugPtr = IntPtr.Zero;

            if (hasDebug)
            {
                if (debugTexture.width != width || debugTexture.height != height)
                {
                    debugTexture.Reinitialize(width, height);
                }
                debugPixels = new Color32[width * height];
                debugHandle = GCHandle.Alloc(debugPixels, GCHandleType.Pinned);
                debugPtr = debugHandle.AddrOfPinnedObject();
            }

            bool success = TryRecognizeRaw(srcPtr, width, height, false, outBoard, outCorners, debugPtr);

            if (hasDebug && debugPixels != null)
            {
                debugTexture.SetPixels32(debugPixels);
                debugTexture.Apply();
            }

            return success;
        }
        finally
        {
            if (srcHandle.IsAllocated) srcHandle.Free();
            if (debugHandle.IsAllocated) debugHandle.Free();
        }
    }

    /// <summary>
    /// WebCamTexture からの認識 (デバッグ画像バッファ出力対応)
    /// </summary>
    public bool TryRecognizeWebCam(
        WebCamTexture webCam,
        DiscColor[,] outBoard,
        Vector2[] outCorners = null,
        Texture2D debugTexture = null
    ) {
        if (webCam == null || !webCam.didUpdateThisFrame || outBoard == null) return false;

        int width = webCam.width;
        int height = webCam.height;
        Color32[] pixels = webCam.GetPixels32();
        GCHandle srcHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);

        Color32[] debugPixels = null;
        GCHandle debugHandle = default;
        bool hasDebug = debugTexture != null;

        try
        {
            IntPtr srcPtr = srcHandle.AddrOfPinnedObject();
            IntPtr debugPtr = IntPtr.Zero;

            if (hasDebug)
            {
                if (debugTexture.width != width || debugTexture.height != height)
                {
                    debugTexture.Reinitialize(width, height);
                }
                debugPixels = new Color32[width * height];
                debugHandle = GCHandle.Alloc(debugPixels, GCHandleType.Pinned);
                debugPtr = debugHandle.AddrOfPinnedObject();
            }

            bool success = TryRecognizeRaw(srcPtr, width, height, false, outBoard, outCorners, debugPtr);

            if (hasDebug && debugPixels != null)
            {
                debugTexture.SetPixels32(debugPixels);
                debugTexture.Apply();
            }

            return success;
        }
        finally
        {
            if (srcHandle.IsAllocated) srcHandle.Free();
            if (debugHandle.IsAllocated) debugHandle.Free();
        }
    }

    /// <summary>
    /// XREAL カメラの YUV420 プレーンから直接 OpenCV で盤面認識とデバッグ描画を実行 (ゼロ GPU Roundtrip)
    /// </summary>
    public bool TryRecognizeYuvPlanes(
        Unity.Collections.NativeArray<byte> yNative,
        Unity.Collections.NativeArray<byte> uNative,
        Unity.Collections.NativeArray<byte> vNative,
        int width,
        int height,
        DiscColor[,] outBoard,
        Vector2[] outCorners,
        Texture2D debugTexture,
        int outWidth = 640,
        int outHeight = 360
    ) {
        if (!yNative.IsCreated || !uNative.IsCreated || !vNative.IsCreated || width <= 0 || height <= 0 || outBoard == null)
        {
            return false;
        }

        if (_yBuffer == null || _yBuffer.Length != yNative.Length)
        {
            _yBuffer = new byte[yNative.Length];
            _uBuffer = new byte[uNative.Length];
            _vBuffer = new byte[vNative.Length];
        }

        yNative.CopyTo(_yBuffer);
        uNative.CopyTo(_uBuffer);
        vNative.CopyTo(_vBuffer);

        GCHandle yHandle = GCHandle.Alloc(_yBuffer, GCHandleType.Pinned);
        GCHandle uHandle = GCHandle.Alloc(_uBuffer, GCHandleType.Pinned);
        GCHandle vHandle = GCHandle.Alloc(_vBuffer, GCHandleType.Pinned);

        bool hasDebug = debugTexture != null;
        GCHandle debugHandle = default;

        try
        {
            IntPtr yPtr = yHandle.AddrOfPinnedObject();
            IntPtr uPtr = uHandle.AddrOfPinnedObject();
            IntPtr vPtr = vHandle.AddrOfPinnedObject();
            IntPtr debugPtr = IntPtr.Zero;

            if (hasDebug)
            {
                if (debugTexture.width != outWidth || debugTexture.height != outHeight)
                {
                    debugTexture.Reinitialize(outWidth, outHeight);
                }
                if (_yuvDebugPixels == null || _yuvDebugPixels.Length != outWidth * outHeight)
                {
                    _yuvDebugPixels = new Color32[outWidth * outHeight];
                }
                debugHandle = GCHandle.Alloc(_yuvDebugPixels, GCHandleType.Pinned);
                debugPtr = debugHandle.AddrOfPinnedObject();
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            int result = OthelloCv_RecognizeBoardYUV(
                yPtr, uPtr, vPtr,
                width, height,
                _boardBuffer, _cornerBuffer,
                debugPtr, outWidth, outHeight,
                WarpSize, MinAreaThreshold, BlackThreshold, WhiteThreshold,
                TransparentBackground ? 1 : 0
            );

            if (result == 1)
            {
                for (int y = 0; y < 8; ++y)
                {
                    for (int x = 0; x < 8; ++x)
                    {
                        outBoard[y, x] = (DiscColor)_boardBuffer[y * 8 + x];
                    }
                }
                if (outCorners != null && outCorners.Length >= 4)
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        outCorners[i] = new Vector2(_cornerBuffer[i * 2], _cornerBuffer[i * 2 + 1]);
                    }
                }
            }

            if (hasDebug && _yuvDebugPixels != null)
            {
                debugTexture.SetPixels32(_yuvDebugPixels);
                debugTexture.Apply();
            }

            return result == 1;
#else
            // Editor シミュレーション
            for (int y = 0; y < 8; ++y)
            {
                for (int x = 0; x < 8; ++x)
                {
                    outBoard[y, x] = DiscColor.None;
                }
            }
            outBoard[3, 3] = DiscColor.White;
            outBoard[4, 4] = DiscColor.White;
            outBoard[3, 4] = DiscColor.Black;
            outBoard[4, 3] = DiscColor.Black;

            if (hasDebug && _yuvDebugPixels != null)
            {
                debugTexture.SetPixels32(_yuvDebugPixels);
                debugTexture.Apply();
            }
            return true;
#endif
        }
        finally
        {
            if (yHandle.IsAllocated) yHandle.Free();
            if (uHandle.IsAllocated) uHandle.Free();
            if (vHandle.IsAllocated) vHandle.Free();
            if (debugHandle.IsAllocated) debugHandle.Free();
        }
    }

    /// <summary>
    /// XREAL カメラドライバから取得したハードウェアネイティブポインタを直接 OpenCV へ渡し認識および描画 (ゼロコピー)
    /// </summary>
    public bool TryRecognizeNativeYuvPlanes(
        IntPtr yPtr,
        IntPtr uPtr,
        IntPtr vPtr,
        int width,
        int height,
        DiscColor[,] outBoard,
        Vector2[] outCorners,
        Texture2D debugTexture,
        int outWidth = 640,
        int outHeight = 360
    ) {
        if (yPtr == IntPtr.Zero || uPtr == IntPtr.Zero || vPtr == IntPtr.Zero || width <= 0 || height <= 0 || outBoard == null)
        {
            return false;
        }

        bool hasDebug = debugTexture != null;
        int debugByteCount = outWidth * outHeight * 4;

        if (hasDebug)
        {
            if (debugTexture.width != outWidth || debugTexture.height != outHeight)
            {
                debugTexture.Reinitialize(outWidth, outHeight);
            }
            if (_nativeDebugByteBuffer == null || _nativeDebugByteBuffer.Length != debugByteCount)
            {
                if (_nativeDebugHandle.IsAllocated) _nativeDebugHandle.Free();
                _nativeDebugByteBuffer = new byte[debugByteCount];
                _nativeDebugHandle = GCHandle.Alloc(_nativeDebugByteBuffer, GCHandleType.Pinned);
            }
        }

        IntPtr debugPtr = (hasDebug && _nativeDebugHandle.IsAllocated) ? _nativeDebugHandle.AddrOfPinnedObject() : IntPtr.Zero;

#if UNITY_ANDROID && !UNITY_EDITOR
        int result = OthelloCv_RecognizeBoardYUV(
            yPtr, uPtr, vPtr,
            width, height,
            _boardBuffer, _cornerBuffer,
            debugPtr, outWidth, outHeight,
            WarpSize, MinAreaThreshold, BlackThreshold, WhiteThreshold,
            TransparentBackground ? 1 : 0
        );

        if (result == 1)
        {
            for (int y = 0; y < 8; ++y)
            {
                for (int x = 0; x < 8; ++x)
                {
                    outBoard[y, x] = (DiscColor)_boardBuffer[y * 8 + x];
                }
            }
            if (outCorners != null && outCorners.Length >= 4)
            {
                for (int i = 0; i < 4; ++i)
                {
                    outCorners[i] = new Vector2(_cornerBuffer[i * 2], _cornerBuffer[i * 2 + 1]);
                }
            }
        }

        if (hasDebug && debugPtr != IntPtr.Zero)
        {
            debugTexture.LoadRawTextureData(debugPtr, debugByteCount);
            debugTexture.Apply();
        }

        return result == 1;
#else
        // Editor シミュレーション
        for (int y = 0; y < 8; ++y)
        {
            for (int x = 0; x < 8; ++x)
            {
                outBoard[y, x] = DiscColor.None;
            }
        }
        outBoard[3, 3] = DiscColor.White;
        outBoard[4, 4] = DiscColor.White;
        outBoard[3, 4] = DiscColor.Black;
        outBoard[4, 3] = DiscColor.Black;

        if (hasDebug && _nativeDebugByteBuffer != null && debugPtr != IntPtr.Zero)
        {
            if (TransparentBackground)
            {
                // 完全透過背景
                Array.Clear(_nativeDebugByteBuffer, 0, debugByteCount);

                // エディタシミュレーション用の黄色枠線 (4隅の矩形)
                int bx0 = outWidth * 2 / 10;
                int bx1 = outWidth * 8 / 10;
                int by0 = outHeight * 2 / 10;
                int by1 = outHeight * 8 / 10;

                void SetPix(int px, int py, byte r, byte g, byte b, byte a)
                {
                    if (px >= 0 && px < outWidth && py >= 0 && py < outHeight)
                    {
                        int idx = (py * outWidth + px) * 4;
                        _nativeDebugByteBuffer[idx + 0] = r;
                        _nativeDebugByteBuffer[idx + 1] = g;
                        _nativeDebugByteBuffer[idx + 2] = b;
                        _nativeDebugByteBuffer[idx + 3] = a;
                    }
                }

                // 黄色い枠線描画 (太さ 4)
                for (int t = 0; t < 4; t++)
                {
                    for (int x = bx0; x <= bx1; x++)
                    {
                        SetPix(x, by0 + t, 255, 255, 0, 255);
                        SetPix(x, by1 - t, 255, 255, 0, 255);
                    }
                    for (int y = by0; y <= by1; y++)
                    {
                        SetPix(bx0 + t, y, 255, 255, 0, 255);
                        SetPix(bx1 - t, y, 255, 255, 0, 255);
                    }
                }
            }
            else
            {
                for (int i = 0; i < debugByteCount; i += 4)
                {
                    _nativeDebugByteBuffer[i + 0] = 0;   // R
                    _nativeDebugByteBuffer[i + 1] = 180; // G
                    _nativeDebugByteBuffer[i + 2] = 220; // B
                    _nativeDebugByteBuffer[i + 3] = 255; // A
                }
            }
            debugTexture.LoadRawTextureData(debugPtr, debugByteCount);
            debugTexture.Apply();
        }
        return true;
#endif
    }

    /// <summary>
    /// 従来の WebCamTexture 認識互換メソッド
    /// </summary>
    public bool TryRecognize(WebCamTexture webCam, DiscColor[,] outBoard, Vector2[] outCorners = null)
    {
        return TryRecognizeWebCam(webCam, outBoard, outCorners, null);
    }

    public void Dispose()
    {
        if (_nativeDebugHandle.IsAllocated)
        {
            _nativeDebugHandle.Free();
        }
    }
}
