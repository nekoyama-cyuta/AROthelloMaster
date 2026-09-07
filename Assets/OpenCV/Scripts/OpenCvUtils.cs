using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using OpenCvSharp;

namespace OpenCVForUnityCustom
{
    /// <summary>
    /// Unity (Texture2D / WebCamTexture) と OpenCV (Mat) の相互変換および連携用ユーティリティクラス
    /// </summary>
    public static class OpenCvUtils
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);
#endif

        private static bool _initialized = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void InitializeNativeDll()
        {
            if (_initialized) return;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                // Unity Editor または Standalone の実行パスからネイティブ DLL ディレクトリを設定
                string pluginDir = Path.Combine(Application.dataPath, "OpenCV", "Plugins", "x86_64");
                if (Directory.Exists(pluginDir))
                {
                    SetDllDirectory(pluginDir);
                    Debug.Log($"[OpenCvUtils] Native DLL directory registered: {pluginDir}");
                }
                else
                {
                    // Plugins 直下の場合のフォールバック
                    string fallbackDir = Path.Combine(Application.dataPath, "Plugins", "x86_64");
                    if (Directory.Exists(fallbackDir))
                    {
                        SetDllDirectory(fallbackDir);
                        Debug.Log($"[OpenCvUtils] Native DLL directory registered (fallback): {fallbackDir}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OpenCvUtils] Failed to set DLL directory: {ex.Message}");
            }
#endif
            _initialized = true;
        }

        static OpenCvUtils()
        {
            InitializeNativeDll();
        }

        /// <summary>
        /// Texture2D を OpenCV の Mat (8UC4: BGRA) に変換します。
        /// Unity のテクスチャ座標 (Y軸下原点) は OpenCV (Y軸上原点) に合わせて自動的に上下反転されます。
        /// </summary>
        public static Mat ToMat(this Texture2D texture)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));

            InitializeNativeDll();

            Color32[] pixels = texture.GetPixels32();
            int width = texture.width;
            int height = texture.height;

            Mat bgraMat = new Mat(height, width, MatType.CV_8UC4);
            GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using (Mat rgbaMat = Mat.FromPixelData(height, width, MatType.CV_8UC4, handle.AddrOfPinnedObject()))
                {
                    Cv2.CvtColor(rgbaMat, bgraMat, ColorConversionCodes.RGBA2BGRA);
                }
            }
            finally
            {
                handle.Free();
            }

            // Unity はテクスチャ座標が上下反転しているため、OpenCV 座標に合わせて上下反転
            Cv2.Flip(bgraMat, bgraMat, FlipMode.X);

            return bgraMat;
        }

        /// <summary>
        /// OpenCV の Mat を Unity の新しい Texture2D (RGBA32) に変換します。
        /// </summary>
        public static Texture2D ToTexture2D(this Mat mat)
        {
            if (mat == null || mat.IsDisposed)
                throw new ArgumentNullException(nameof(mat));

            InitializeNativeDll();

            Texture2D texture = new Texture2D(mat.Width, mat.Height, TextureFormat.RGBA32, false);
            MatToTexture2D(mat, texture);
            return texture;
        }

        /// <summary>
        /// OpenCV の Mat の内容を既存の Texture2D に高速コピーして更新します。(GC Alloc最小化)
        /// テクスチャサイズが Mat と一致している必要があります。
        /// </summary>
        public static void MatToTexture2D(Mat mat, Texture2D target)
        {
            if (mat == null || mat.IsDisposed)
                throw new ArgumentNullException(nameof(mat));
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            InitializeNativeDll();

            if (target.width != mat.Width || target.height != mat.Height)
            {
                target.Reinitialize(mat.Width, mat.Height, TextureFormat.RGBA32, false);
            }

            using (Mat flipped = new Mat())
            using (Mat rgbaMat = new Mat())
            {
                // 上下反転
                Cv2.Flip(mat, flipped, FlipMode.X);

                if (flipped.Channels() == 1)
                {
                    Cv2.CvtColor(flipped, rgbaMat, ColorConversionCodes.GRAY2RGBA);
                }
                else if (flipped.Channels() == 3)
                {
                    Cv2.CvtColor(flipped, rgbaMat, ColorConversionCodes.BGR2RGBA);
                }
                else if (flipped.Channels() == 4)
                {
                    Cv2.CvtColor(flipped, rgbaMat, ColorConversionCodes.BGRA2RGBA);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported channel count: {flipped.Channels()}");
                }

                int bufferSize = (int)(rgbaMat.Total() * rgbaMat.ElemSize());
                target.LoadRawTextureData(rgbaMat.Data, bufferSize);
                target.Apply();
            }
        }

        /// <summary>
        /// WebCamTexture の現在フレームを OpenCV の Mat (BGRA) にコピーします。
        /// </summary>
        public static void WebCamTextureToMat(WebCamTexture webCam, Mat outputMat)
        {
            if (webCam == null || !webCam.isPlaying)
                throw new InvalidOperationException("WebCamTexture is not active.");
            if (outputMat == null || outputMat.IsDisposed)
                throw new ArgumentNullException(nameof(outputMat));

            InitializeNativeDll();

            Color32[] pixels = webCam.GetPixels32();
            int width = webCam.width;
            int height = webCam.height;

            if (outputMat.Width != width || outputMat.Height != height || outputMat.Type() != MatType.CV_8UC4)
            {
                outputMat.Create(height, width, MatType.CV_8UC4);
            }

            GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using (Mat rgba = Mat.FromPixelData(height, width, MatType.CV_8UC4, handle.AddrOfPinnedObject()))
                {
                    Cv2.CvtColor(rgba, outputMat, ColorConversionCodes.RGBA2BGRA);
                }
            }
            finally
            {
                handle.Free();
            }

            if (!webCam.videoVerticallyMirrored)
            {
                Cv2.Flip(outputMat, outputMat, FlipMode.X);
            }
        }
    }
}
