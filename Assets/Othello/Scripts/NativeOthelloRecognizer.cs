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

    [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int OthelloCv_GetLastCellStats(
        [Out] float[] outValues,
        [Out] float[] outSaturations,
        [Out] float[] outHues
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

    #region 統計学的動的閾値キャリブレーション (多クラス大津法 / Multi-Otsu)

    [Serializable]
    public struct CalibrationResult
    {
        public int BlackThreshold;
        public int WhiteThreshold;
        public float Separability;       // η: 分離度指標 (0..1)
        public float FeltMean;          // フェルト平均輝度
        public float FeltStdDev;        // フェルト標準偏差
        public float Class0Mean;        // 黒石クラスタ平均
        public float Class1Mean;        // フェルトクラスタ平均
        public float Class2Mean;        // 白石クラスタ平均
        public bool IsDegenerate;       // 空盤面または単一峰検定フラグ (3.5σ安全保護適用)
        public string Summary;

        public override string ToString()
        {
            return $"B<{BlackThreshold} | W>{WhiteThreshold} | η:{Separability:F2} | Felt:{FeltMean:F1}±{FeltStdDev:F1}" +
                   (IsDegenerate ? " [Degenerate 3.5σ]" : " [Multi-Otsu]");
        }
    }

    public CalibrationResult LastCalibration { get; private set; }

    private readonly float[] _cachedCellValues = new float[64];
    private readonly float[] _cachedCellSaturations = new float[64];
    private readonly float[] _cachedCellHues = new float[64];

    /// <summary>
    /// 直近の認識フレームから 64 マスの HSV 統計量を取得
    /// </summary>
    public bool TryGetLastCellStats(float[] outValues, float[] outSaturations = null, float[] outHues = null)
    {
        if (outValues == null || outValues.Length < 64) return false;

#if UNITY_ANDROID && !UNITY_EDITOR
        int res = OthelloCv_GetLastCellStats(outValues, outSaturations, outHues);
        return res == 1;
#else
        // Editor シミュレーション: 初期配置 (フェルト ~110, 白石 ~220, 黒石 ~25)
        for (int i = 0; i < 64; i++)
        {
            outValues[i] = 110f + UnityEngine.Random.Range(-8f, 8f);
            if (outSaturations != null && outSaturations.Length >= 64)
            {
                outSaturations[i] = 120f + UnityEngine.Random.Range(-10f, 10f); // 緑の彩度
            }
            if (outHues != null && outHues.Length >= 64)
            {
                outHues[i] = 60f + UnityEngine.Random.Range(-5f, 5f); // 緑相
            }
        }
        // 白石 (3,3), (4,4)
        outValues[3 * 8 + 3] = 225f;
        outValues[4 * 8 + 4] = 220f;
        // 黒石 (3,4), (4,3)
        outValues[3 * 8 + 4] = 25f;
        outValues[4 * 8 + 3] = 28f;

        if (outSaturations != null)
        {
            outSaturations[3 * 8 + 3] = 15f; // 白石は低彩度
            outSaturations[4 * 8 + 4] = 18f;
            outSaturations[3 * 8 + 4] = 20f; // 黒石も低彩度
            outSaturations[4 * 8 + 3] = 22f;
        }
        return true;
#endif
    }

    /// <summary>
    /// 直近の認識データまたは指定データから統計的動的閾値 (Multi-Otsu) を計算し、自身の閾値を即時更新
    /// </summary>
    public CalibrationResult CalibrateDynamicThresholds(float[] overrideValues = null, float[] overrideSaturations = null)
    {
        float[] values = overrideValues;
        float[] sats = overrideSaturations;

        if (values == null)
        {
            if (TryGetLastCellStats(_cachedCellValues, _cachedCellSaturations, _cachedCellHues))
            {
                values = _cachedCellValues;
                sats = _cachedCellSaturations;
            }
            else
            {
                // データ未取得時のデフォルト結果
                var def = new CalibrationResult
                {
                    BlackThreshold = this.BlackThreshold,
                    WhiteThreshold = this.WhiteThreshold,
                    Separability = 0f,
                    FeltMean = 110f,
                    FeltStdDev = 10f,
                    IsDegenerate = true,
                    Summary = "No frame data available; keeping defaults."
                };
                LastCalibration = def;
                return def;
            }
        }

        CalibrationResult result = CalibrateMultiOtsu(values, sats);

        // 閾値を動的反映
        this.BlackThreshold = result.BlackThreshold;
        this.WhiteThreshold = result.WhiteThreshold;
        LastCalibration = result;

        UnityEngine.Debug.Log($"[NativeOthelloRecognizer] Calibrated thresholds: {result}");
        return result;
    }

    /// <summary>
    /// 多クラス大津の2値化法 (Multi-Otsu's Thresholding) による最適決定境界の算出
    /// クラス間分散 σB^2 を大域的に最大化する境界 (t_black, t_white) を探索。
    /// 分離度 η またはクラス間距離が不足する場合は 3.5σ 信頼区間ルールへ安全退避。
    /// </summary>
    public static CalibrationResult CalibrateMultiOtsu(float[] values, float[] saturations = null)
    {
        if (values == null || values.Length < 64)
        {
            return new CalibrationResult
            {
                BlackThreshold = 65,
                WhiteThreshold = 165,
                Separability = 0f,
                FeltMean = 110f,
                FeltStdDev = 15f,
                IsDegenerate = true,
                Summary = "Invalid values array"
            };
        }

        int N = values.Length;

        // 1. 全平均 μT と全分散 σT^2 の算出
        double sum = 0.0;
        for (int i = 0; i < N; i++)
        {
            sum += values[i];
        }
        double muT = sum / N;

        double sumSqDiff = 0.0;
        for (int i = 0; i < N; i++)
        {
            double diff = values[i] - muT;
            sumSqDiff += diff * diff;
        }
        double sigmaT2 = sumSqDiff / N;
        double sigmaT = Math.Sqrt(sigmaT2);

        // 2. 256階調のヒストグラム作成
        int[] hist = new int[256];
        for (int i = 0; i < N; i++)
        {
            int bin = Mathf.Clamp(Mathf.RoundToInt(values[i]), 0, 255);
            hist[bin]++;
        }

        // 累積度数 P[v] と累積一次モーメント M[v]
        int[] P = new int[256];
        double[] M = new double[256];
        int cumP = 0;
        double cumM = 0.0;
        for (int v = 0; v < 256; v++)
        {
            cumP += hist[v];
            cumM += v * hist[v];
            P[v] = cumP;
            M[v] = cumM;
        }

        // 3. Multi-Otsu 最適境界 (t1, t2) の大域探索
        // t1: 黒石とフェルトの境目 (0 <= t1 < 254)
        // t2: フェルトと白石の境目 (t1 < t2 < 255)
        double maxSigmaB2 = -1.0;
        int bestT1 = 65;
        int bestT2 = 165;
        double bestMu0 = 0.0;
        double bestMu1 = 0.0;
        double bestMu2 = 0.0;

        for (int t1 = 5; t1 <= 245; t1++)
        {
            int n0 = P[t1];
            if (n0 == 0) continue;
            double w0 = (double)n0 / N;
            double u0 = M[t1] / n0;

            for (int t2 = t1 + 6; t2 <= 250; t2++)
            {
                int n1 = P[t2] - n0;
                int n2 = N - P[t2];
                if (n1 == 0 || n2 == 0) continue;

                double w1 = (double)n1 / N;
                double w2 = (double)n2 / N;

                double u1 = (M[t2] - M[t1]) / n1;
                double u2 = (M[255] - M[t2]) / n2;

                // クラス間分散 σB^2 = w0*(u0-muT)^2 + w1*(u1-muT)^2 + w2*(u2-muT)^2
                double sb2 = w0 * (u0 - muT) * (u0 - muT) +
                             w1 * (u1 - muT) * (u1 - muT) +
                             w2 * (u2 - muT) * (u2 - muT);

                if (sb2 > maxSigmaB2)
                {
                    maxSigmaB2 = sb2;
                    bestT1 = t1;
                    bestT2 = t2;
                    bestMu0 = u0;
                    bestMu1 = u1;
                    bestMu2 = u2;
                }
            }
        }

        // 分離度指標 η = σB^2 / σT^2
        float eta = (sigmaT2 > 1e-4) ? (float)(maxSigmaB2 / sigmaT2) : 0f;
        eta = Mathf.Clamp01(eta);

        // フェルトの平均および標準偏差の精密推定 (中央クラスまたは高彩度マス)
        double feltSum = 0.0;
        int feltCount = 0;
        for (int i = 0; i < N; i++)
        {
            float v = values[i];
            bool isChroma = (saturations != null && i < saturations.Length && saturations[i] >= 35f);
            if (isChroma || (v > bestT1 && v < bestT2))
            {
                feltSum += v;
                feltCount++;
            }
        }

        double feltMean = (feltCount > 0) ? (feltSum / feltCount) : muT;
        double feltSqDiff = 0.0;
        for (int i = 0; i < N; i++)
        {
            float v = values[i];
            bool isChroma = (saturations != null && i < saturations.Length && saturations[i] >= 35f);
            if (isChroma || (v > bestT1 && v < bestT2))
            {
                double d = v - feltMean;
                feltSqDiff += d * d;
            }
        }
        double feltStdDev = (feltCount > 1) ? Math.Sqrt(feltSqDiff / feltCount) : Math.Max(sigmaT, 8.0);

        // 4. 退化検定 (Degenerate Case Test)
        // 石が存在しない空盤面、または明瞭な3峰性がない (η < 0.60 またはクラス間距離不足) 場合
        bool isDegenerate = (eta < 0.60f) || (bestMu2 - bestMu0 < 45.0) || (sigmaT2 < 120.0);

        int finalTBlack;
        int finalTWhite;

        if (isDegenerate)
        {
            // 3.5σ 信頼区間ルールによる安全境界
            // フェルトの正規分布から外れる確率 0.02% 未満に設定し、空盤面でフェルトを誤検知させない
            int safeBlack = Mathf.RoundToInt((float)(feltMean - 3.5 * feltStdDev));
            int safeWhite = Mathf.RoundToInt((float)(feltMean + 3.5 * feltStdDev));

            finalTBlack = Mathf.Clamp(safeBlack, 20, 95);
            finalTWhite = Mathf.Clamp(safeWhite, 135, 235);
        }
        else
        {
            // 3峰クラスタが明瞭な場合:
            // Multi-Otsu により求めた各クラス重心 (bestMu0:黒石, bestMu1:フェルト, bestMu2:白石) に対し、
            // オセロ盤特有のサンプル数不均衡 (フェルト50マス vs 石数マス) による境界の偏りを補正。
            // クラス間のちょうど境目 (Midpoint) に決定境界を線引き。
            int midBlack = Mathf.RoundToInt((float)((bestMu0 + bestMu1) * 0.5));
            int midWhite = Mathf.RoundToInt((float)((bestMu1 + bestMu2) * 0.5));

            // フェルトの分散 (3.0σ) との整合性を考慮した安全クリップ
            int minSafeBlack = Mathf.RoundToInt((float)(feltMean - 3.2 * feltStdDev));
            int maxSafeBlack = Mathf.RoundToInt((float)(feltMean - 1.5 * feltStdDev));
            int minSafeWhite = Mathf.RoundToInt((float)(feltMean + 1.5 * feltStdDev));
            int maxSafeWhite = Mathf.RoundToInt((float)(feltMean + 3.2 * feltStdDev));

            finalTBlack = Mathf.Clamp(midBlack, Mathf.Max(12, minSafeBlack), Mathf.Max(15, maxSafeBlack));
            finalTWhite = Mathf.Clamp(midWhite, Mathf.Min(235, minSafeWhite), Mathf.Min(242, maxSafeWhite));
        }

        var result = new CalibrationResult
        {
            BlackThreshold = finalTBlack,
            WhiteThreshold = finalTWhite,
            Separability = eta,
            FeltMean = (float)feltMean,
            FeltStdDev = (float)feltStdDev,
            Class0Mean = (float)bestMu0,
            Class1Mean = (float)bestMu1,
            Class2Mean = (float)bestMu2,
            IsDegenerate = isDegenerate,
            Summary = $"B<{finalTBlack}, W>{finalTWhite}, η={eta:F2}"
        };

        return result;
    }

    #endregion

    public void Dispose()
    {
        if (_nativeDebugHandle.IsAllocated)
        {
            _nativeDebugHandle.Free();
        }
    }
}
