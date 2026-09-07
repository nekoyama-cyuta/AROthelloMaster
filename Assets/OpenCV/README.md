# OpenCV for Unity 開発環境 & サンプルガイド

本ディレクトリには、Unity 6 内で **OpenCV (OpenCvSharp4)** を利用するためのネイティブプラグイン、マネージドアセンブリ、相互変換ユーティリティ、および動作確認用サンプルが含まれています。

既存のプロジェクトファイルには一切変更を加えていません。

---

## 📁 ディレクトリ構成

```text
Assets/OpenCV/
├── Plugins/
│   ├── OpenCvSharp.dll                       # OpenCvSharp4 マネージドアセンブリ (.NET Standard 2.1)
│   ├── System.Runtime.CompilerServices.Unsafe.dll
│   └── x86_64/
│       ├── OpenCvSharpExtern.dll             # OpenCV ネイティブ C++ バイナリ (Windows 64bit)
│       └── opencv_videoio_ffmpeg4130_64.dll  # 動画・カメラ IO 用 FFmpeg バイナリ
├── Scripts/
│   ├── OpenCvUtils.cs                       # Texture2D / WebCamTexture と Mat の相互変換ユーティリティ
│   ├── OpenCvDemoController.cs              # デモ用コントローラー（UI自動生成・8種の画像処理）
│   └── WebCamOpenCvProcessor.cs             # 汎用Webカメラ画像処理コンポーネント
├── Scenes/
│   └── OpenCvDemoScene.unity                # 動作確認用デモシーン
└── README.md                                # 本ドキュメント
```

---

## 🚀 サンプルの動かし方

1. **Unity Editor** で本プロジェクトを開きます。
2. Project ウィンドウから `Assets/OpenCV/Scenes/OpenCvDemoScene.unity` をダブルクリックして開きます。
3. **Play（再生ボタン）** を押します。
4. 画面上に自動生成されたUIと、オセロ盤・石を模したプロシージャルテスト画像が表示されます。
5. 左側のボタンをクリックして、各種 OpenCV フィルタの適用結果と処理時間 (ms) を確認できます：
   - **1. Original**: 元画像のプレビュー
   - **2. Grayscale**: グレースケール変換 (`Cv2.CvtColor`)
   - **3. Gaussian Blur**: ガウシアンぼかし (`Cv2.GaussianBlur`)
   - **4. Canny Edge**: エッジ検出 (`Cv2.Canny`)
   - **5. Threshold**: 大津の二値化 (`Cv2.Threshold` + `Otsu`)
   - **6. Contours**: 輪郭抽出と緑色枠線描画 (`Cv2.FindContours`, `Cv2.DrawContours`)
   - **7. Circles**: ハフ変換による円・石の検出と描画 (`Cv2.HoughCircles`)
   - **8. Invert**: ネガポジ反転 (`Cv2.BitwiseNot`)
   - **Camera: OFF / ON**: PC に Web カメラが接続されている場合、カメラ映像に対してリアルタイムで上記の各画像処理を適用可能。

---

## 💻 スクリプトでの使い方 (コード例)

### 1. `Texture2D` から `Mat` への変換
```csharp
using UnityEngine;
using OpenCvSharp;
using OpenCVForUnityCustom;

public class Example : MonoBehaviour
{
    public Texture2D sourceTexture;

    void ProcessImage()
    {
        // Texture2D -> Mat (BGRA) に自動変換 (上下反転補正付き)
        using (Mat srcMat = sourceTexture.ToMat())
        using (Mat grayMat = new Mat())
        {
            // グレースケール変換
            Cv2.CvtColor(srcMat, grayMat, ColorConversionCodes.BGRA2GRAY);

            // Mat -> 新規 Texture2D
            Texture2D resultTexture = grayMat.ToTexture2D();
        }
    }
}
```

### 2. 既存の `Texture2D` への高速転送 (GC Alloc 最小化)
毎フレーム新しい `Texture2D` を生成するとメモリ負荷が高まるため、`MatToTexture2D` を使って既存のテクスチャに書き込みます。
```csharp
using UnityEngine;
using OpenCvSharp;
using OpenCVForUnityCustom;

public class RealtimeExample : MonoBehaviour
{
    private Texture2D _displayTexture;

    void Start()
    {
        _displayTexture = new Texture2D(640, 480, TextureFormat.RGBA32, false);
    }

    void UpdateFrame(Mat processedMat)
    {
        // 既存のテクスチャに直接転送
        OpenCvUtils.MatToTexture2D(processedMat, _displayTexture);
    }
}
```

### 3. ハフ変換による円・オセロ石検出
```csharp
using OpenCvSharp;

void DetectCircles(Mat srcBgra)
{
    using (Mat gray = new Mat())
    {
        Cv2.CvtColor(srcBgra, gray, ColorConversionCodes.BGRA2GRAY);
        Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(9, 9), 2);

        CircleSegment[] circles = Cv2.HoughCircles(
            gray,
            HoughModes.Gradient,
            dp: 1.2,
            minDist: 30,
            param1: 100,
            param2: 30,
            minRadius: 10,
            maxRadius: 100
        );

        foreach (var c in circles)
        {
            Debug.Log($"Detected Circle: Center=({c.Center.X}, {c.Center.Y}), Radius={c.Radius}");
        }
    }
}
```

---

## ⚠️ 重要: メモリ管理 (Dispose の徹底)

- `OpenCvSharp.Mat` は **C++ ネイティブメモリ** を確保します。
- C# のガベージコレクション (GC) のみでは即座に解放されず、フレーム毎に生成するとメモリリークの原因となります。
- **必ず `using` 文を使うか、明示的に `.Dispose()` を呼んでください**。

```csharp
// 推奨パターン: using によるスコープ内自動解放
using (Mat temp = new Mat())
{
    // 処理...
}
```
