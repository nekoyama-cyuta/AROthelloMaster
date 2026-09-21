using System;
using OpenCvSharp;

namespace OpenCVForUnityCustom
{
    public enum DiscColor { None = 0, Black = 1, White = 2 }

    public class OthelloBoardRecognizer : IDisposable
    {
        private readonly int _warpSize;
        
        // 再利用バッファ (ゼロGC)
        private Mat _hsvMat;
        private Mat _maskMat;
        private Mat _morphKernel;
        private Mat _warpedMat;

        private readonly Point2f[] _dstCorners;

        public OthelloBoardRecognizer(int warpSize = 400)
        {
            _warpSize = warpSize;
            _hsvMat = new Mat();
            _maskMat = new Mat();
            _warpedMat = new Mat();
            _morphKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));

            _dstCorners = new Point2f[]
            {
                new Point2f(0, 0),
                new Point2f(_warpSize, 0),
                new Point2f(_warpSize, _warpSize),
                new Point2f(0, _warpSize)
            };
        }

        public bool TryRecognizeBoard(Mat srcBgraMat, DiscColor[,] outBoard, Mat debugMat = null)
        {
            if (srcBgraMat == null || srcBgraMat.IsDisposed) return false;

            if (debugMat != null)
            {
                srcBgraMat.CopyTo(debugMat);
            }

            // 1. 緑色マスク抽出
            Cv2.CvtColor(srcBgraMat, _hsvMat, ColorConversionCodes.BGRA2BGR);
            Cv2.CvtColor(_hsvMat, _hsvMat, ColorConversionCodes.BGR2HSV);
            Cv2.InRange(_hsvMat, new Scalar(35, 50, 50), new Scalar(85, 255, 255), _maskMat);
            Cv2.MorphologyEx(_maskMat, _maskMat, MorphTypes.Close, _morphKernel);

            // 2. 輪郭抽出
            Cv2.FindContours(_maskMat, out Point[][] contours, out _, 
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            Point[] bestQuad = null;
            double maxArea = 8000;

            foreach (var cnt in contours)
            {
                double perimeter = Cv2.ArcLength(cnt, true);
                Point[] approx = Cv2.ApproxPolyDP(cnt, perimeter * 0.03, true);

                if (approx.Length == 4 && Cv2.IsContourConvex(approx))
                {
                    double area = Cv2.ContourArea(approx);
                    if (area > maxArea)
                    {
                        maxArea = area;
                        bestQuad = approx;
                    }
                }
            }

            if (bestQuad == null) return false;

            // 3. パース変換 (Perspective Warp)
            Point2f[] srcCorners = SortCorners(bestQuad);
            using (Mat M = Cv2.GetPerspectiveTransform(srcCorners, _dstCorners))
            using (Mat invM = Cv2.GetPerspectiveTransform(_dstCorners, srcCorners))
            {
                Cv2.WarpPerspective(srcBgraMat, _warpedMat, M, new Size(_warpSize, _warpSize));

                // 4. 8x8 マス分割 ＆ 石の判定
                int cellSize = _warpSize / 8;
                int margin = cellSize / 4;

                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        Rect roi = new Rect(x * cellSize + margin, y * cellSize + margin, 
                                            cellSize - margin * 2, cellSize - margin * 2);

                        using (Mat cell = new Mat(_warpedMat, roi))
                        {
                            DiscColor disc = ClassifyCell(cell);
                            outBoard[y, x] = disc;

                            // デバッグ用: パース画像側に判定枠を描画
                            if (debugMat != null)
                            {
                                Scalar color = disc switch
                                {
                                    DiscColor.Black => new Scalar(20, 20, 20, 255),
                                    DiscColor.White => new Scalar(240, 240, 240, 255),
                                    _ => new Scalar(0, 180, 0, 255)
                                };
                                Cv2.Rectangle(_warpedMat, roi, color, 1);
                                if (disc != DiscColor.None)
                                {
                                    Point center = new Point(x * cellSize + cellSize / 2, y * cellSize + cellSize / 2);
                                    Cv2.Circle(_warpedMat, center, cellSize / 3, color, -1);
                                    Cv2.Circle(_warpedMat, center, cellSize / 3, new Scalar(0, 0, 255, 255), 1);
                                }
                            }
                        }
                    }
                }

                // 5. デバッグ画面の合成描画
                if (debugMat != null)
                {
                    // (A) カメラ画像上の盤の外枠ラインを描画
                    Cv2.Polylines(debugMat, new[] { bestQuad }, true, new Scalar(0, 255, 255, 255), 3);

                    // (B) 各マスの石をカメラの元画像座標に逆変換してオーバーレイ描画
                    DrawDiscsOnCameraView(debugMat, outBoard, invM, cellSize);

                    // (C) 右上にパース補正画像（ミニマップ）をPicture-in-Picture表示
                    DrawMinimap(debugMat, _warpedMat);
                }
            }

            return true;
        }

        private DiscColor ClassifyCell(Mat bgrCell)
        {
            Scalar mean = Cv2.Mean(bgrCell);
            // 輝度（明度）による3値分類
            double brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;

            // 環境光に合わせて適宜調整
            if (brightness < 65) return DiscColor.Black;
            if (brightness > 165) return DiscColor.White;
            return DiscColor.None;
        }

        private void DrawDiscsOnCameraView(Mat debugMat, DiscColor[,] board, Mat invM, int cellSize)
        {
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    DiscColor disc = board[y, x];
                    if (disc == DiscColor.None) continue;

                    // パース画像上のセル中心点
                    Point2f centerWarped = new Point2f(x * cellSize + cellSize / 2f, y * cellSize + cellSize / 2f);
                    
                    // 逆行列でカメラ元画像上の座標へ変換
                    Point2f[] srcPt = Cv2.PerspectiveTransform(new[] { centerWarped }, invM);
                    Point centerCam = new Point((int)srcPt[0].X, (int)srcPt[0].Y);

                    // カメラ画像上に石の検出マーカーを描画
                    Scalar fillColor = (disc == DiscColor.Black) ? new Scalar(10, 10, 10, 255) : new Scalar(245, 245, 245, 255);
                    Cv2.Circle(debugMat, centerCam, 10, fillColor, -1);
                    Cv2.Circle(debugMat, centerCam, 10, new Scalar(0, 255, 0, 255), 2); // 緑フチ
                }
            }
        }

        private void DrawMinimap(Mat debugMat, Mat warpedMat)
        {
            int miniW = 160;
            int miniH = 160;
            int padding = 15;

            // 画面の右上に配置
            Rect miniRoi = new Rect(debugMat.Width - miniW - padding, padding, miniW, miniH);

            using (Mat resizedMini = new Mat())
            {
                Cv2.Resize(warpedMat, resizedMini, new Size(miniW, miniH));
                // アルファチャンネルの互換性を整えてコピー
                if (resizedMini.Channels() == debugMat.Channels())
                {
                    resizedMini.CopyTo(new Mat(debugMat, miniRoi));
                }
                else
                {
                    using (Mat temp = new Mat())
                    {
                        Cv2.CvtColor(resizedMini, temp, ColorConversionCodes.BGR2BGRA);
                        temp.CopyTo(new Mat(debugMat, miniRoi));
                    }
                }
                // ミニマップの外枠を描画
                Cv2.Rectangle(debugMat, miniRoi, new Scalar(255, 255, 255, 255), 2);
            }
        }

        private Point2f[] SortCorners(Point[] pts)
        {
            Point2f[] sorted = new Point2f[4];
            Array.Sort(pts, (a, b) => (a.X + a.Y).CompareTo(b.X + b.Y));
            sorted[0] = new Point2f(pts[0].X, pts[0].Y);
            sorted[2] = new Point2f(pts[3].X, pts[3].Y);

            if (pts[1].X - pts[1].Y > pts[2].X - pts[2].Y)
            {
                sorted[1] = new Point2f(pts[1].X, pts[1].Y);
                sorted[3] = new Point2f(pts[2].X, pts[2].Y);
            }
            else
            {
                sorted[1] = new Point2f(pts[2].X, pts[2].Y);
                sorted[3] = new Point2f(pts[1].X, pts[1].Y);
            }
            return sorted;
        }

        public void Dispose()
        {
            _hsvMat?.Dispose();
            _maskMat?.Dispose();
            _warpedMat?.Dispose();
            _morphKernel?.Dispose();
        }
    }
}