#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>
#include <opencv2/calib3d.hpp>
#include <algorithm>
#include <vector>
#include <cmath>
#include <cstring>

#if defined(_WIN32) || defined(_WIN64)
#define EXPORT_API __declspec(dllexport)
#else
#define EXPORT_API __attribute__((visibility("default")))
#endif

namespace {

// 4隅の座標を並べ替え (0:左上, 1:右上, 2:右下, 3:左下)
void SortCorners(const std::vector<cv::Point>& pts, cv::Point2f outCorners[4]) {
    std::vector<cv::Point> sorted = pts;
    std::sort(sorted.begin(), sorted.end(), [](const cv::Point& a, const cv::Point& b) {
        return (a.x + a.y) < (b.x + b.y);
    });

    outCorners[0] = cv::Point2f(static_cast<float>(sorted[0].x), static_cast<float>(sorted[0].y));
    outCorners[2] = cv::Point2f(static_cast<float>(sorted[3].x), static_cast<float>(sorted[3].y));

    if ((sorted[1].x - sorted[1].y) > (sorted[2].x - sorted[2].y)) {
        outCorners[1] = cv::Point2f(static_cast<float>(sorted[1].x), static_cast<float>(sorted[1].y));
        outCorners[3] = cv::Point2f(static_cast<float>(sorted[2].x), static_cast<float>(sorted[2].y));
    } else {
        outCorners[1] = cv::Point2f(static_cast<float>(sorted[2].x), static_cast<float>(sorted[2].y));
        outCorners[3] = cv::Point2f(static_cast<float>(sorted[1].x), static_cast<float>(sorted[1].y));
    }
}

// BGR 画像に対するオセロ盤面認識 & 描画の共通処理
int ProcessBgrImage(
    cv::Mat& bgr,
    int procW,
    int procH,
    int* outBoard,
    float* outCorners,
    unsigned char* outDebugPixels,
    int warpSize,
    int minAreaThreshold,
    int blackThreshold,
    int whiteThreshold,
    int transparentBg = 0
) {
    if (warpSize <= 0) warpSize = 400;
    if (minAreaThreshold <= 0) minAreaThreshold = 5000;
    if (blackThreshold <= 0) blackThreshold = 65;
    if (whiteThreshold <= 0) whiteThreshold = 165;

    // 1. HSV 変換 & 緑色マスク抽出 (オセロ盤のフェルト緑)
    cv::Mat hsv;
    cv::cvtColor(bgr, hsv, cv::COLOR_BGR2HSV);

    cv::Mat mask;
    cv::inRange(hsv, cv::Scalar(35, 50, 50), cv::Scalar(85, 255, 255), mask);

    cv::Mat morphKernel = cv::getStructuringElement(cv::MORPH_RECT, cv::Size(5, 5));
    cv::morphologyEx(mask, mask, cv::MORPH_CLOSE, morphKernel);

    // 2. 輪郭抽出
    std::vector<std::vector<cv::Point>> contours;
    cv::findContours(mask, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);

    std::vector<cv::Point> bestQuad;
    double maxArea = static_cast<double>(minAreaThreshold);

    for (const auto& cnt : contours) {
        double perimeter = cv::arcLength(cnt, true);
        std::vector<cv::Point> approx;
        cv::approxPolyDP(cnt, approx, perimeter * 0.03, true);

        if (approx.size() == 4 && cv::isContourConvex(approx)) {
            double area = cv::contourArea(approx);
            if (area > maxArea) {
                maxArea = area;
                bestQuad = approx;
            }
        }
    }

    // デバッグ用 RGBA 画像の作成 (transparentBg == 1 の場合は完全透明背景)
    cv::Mat debugImg;
    if (transparentBg) {
        debugImg = cv::Mat::zeros(procH, procW, CV_8UC4);
    } else {
        cv::cvtColor(bgr, debugImg, cv::COLOR_BGR2RGBA);
    }

    if (bestQuad.size() != 4) {
        // 未検出時: 盤面クリア
        std::fill_n(outBoard, 64, 0);

        if (outDebugPixels != nullptr) {
            // Canny エッジ検出 (シアン色合成)
            cv::Mat gray;
            cv::cvtColor(bgr, gray, cv::COLOR_BGR2GRAY);
            cv::Mat edges;
            cv::Canny(gray, edges, 50, 150);

            for (int y = 0; y < procH; ++y) {
                const unsigned char* edgePtr = edges.ptr<unsigned char>(y);
                unsigned char* debugPtr = debugImg.ptr<unsigned char>(y);
                for (int x = 0; x < procW; ++x) {
                    if (edgePtr[x] > 0) {
                        debugPtr[x * 4 + 0] = 0;   // R
                        debugPtr[x * 4 + 1] = 255; // G
                        debugPtr[x * 4 + 2] = 255; // B
                        debugPtr[x * 4 + 3] = 255; // A
                    }
                }
            }

            // 緑色輪郭があれば描画
            if (!contours.empty()) {
                cv::drawContours(debugImg, contours, -1, cv::Scalar(0, 255, 0, 255), 2);
            }

            std::memcpy(outDebugPixels, debugImg.data, procW * procH * 4);
        }

        return 0;
    }

    // 3. 4隅の並べ替え & パース変換
    cv::Point2f srcCorners[4];
    SortCorners(bestQuad, srcCorners);

    for (int i = 0; i < 4; ++i) {
        outCorners[i * 2 + 0] = srcCorners[i].x;
        outCorners[i * 2 + 1] = srcCorners[i].y;
    }

    cv::Point2f dstCorners[4] = {
        cv::Point2f(0.0f, 0.0f),
        cv::Point2f(static_cast<float>(warpSize), 0.0f),
        cv::Point2f(static_cast<float>(warpSize), static_cast<float>(warpSize)),
        cv::Point2f(0.0f, static_cast<float>(warpSize))
    };

    cv::Mat M = cv::getPerspectiveTransform(srcCorners, dstCorners);
    cv::Mat warped;
    cv::warpPerspective(bgr, warped, M, cv::Size(warpSize, warpSize));

    // 4. 8x8 マス分割 & 石の色判定
    int cellSize = warpSize / 8;
    int margin = cellSize / 4;

    for (int y = 0; y < 8; ++y) {
        for (int x = 0; x < 8; ++x) {
            int rx = x * cellSize + margin;
            int ry = y * cellSize + margin;
            int rw = cellSize - margin * 2;
            int rh = cellSize - margin * 2;

            if (rx + rw > warpSize) rw = warpSize - rx;
            if (ry + rh > warpSize) rh = warpSize - ry;

            cv::Rect roi(rx, ry, rw, rh);
            cv::Mat cell = warped(roi);

            cv::Scalar mean = cv::mean(cell);
            double brightness = (mean[0] + mean[1] + mean[2]) / 3.0;

            int disc = 0; // None
            if (brightness < blackThreshold) {
                disc = 1; // Black
            } else if (brightness > whiteThreshold) {
                disc = 2; // White
            }

            outBoard[y * 8 + x] = disc;
        }
    }

    // 5. 検出結果オーバーレイ描画
    if (outDebugPixels != nullptr) {
        // 黄色ポリライン (太さ 4 で AR でもクッキリ表示)
        std::vector<std::vector<cv::Point>> polys = { bestQuad };
        cv::polylines(debugImg, polys, true, cv::Scalar(255, 255, 0, 255), 4);

        // 検出した石を描画 (白石/黒石 + 視認性の高いネオングリーン輪郭)
        cv::Mat invM = cv::getPerspectiveTransform(dstCorners, srcCorners);
        for (int y = 0; y < 8; ++y) {
            for (int x = 0; x < 8; ++x) {
                int disc = outBoard[y * 8 + x];
                if (disc == 0) continue;

                std::vector<cv::Point2f> centerPt = {
                    cv::Point2f(x * cellSize + cellSize * 0.5f, y * cellSize + cellSize * 0.5f)
                };
                std::vector<cv::Point2f> camPt;
                cv::perspectiveTransform(centerPt, camPt, invM);

                cv::Point center(static_cast<int>(camPt[0].x), static_cast<int>(camPt[0].y));
                cv::Scalar color = (disc == 1) ? cv::Scalar(20, 20, 20, 255) : cv::Scalar(250, 250, 250, 255);
                cv::circle(debugImg, center, 12, color, -1);
                cv::circle(debugImg, center, 12, cv::Scalar(0, 255, 100, 255), 2);
            }
        }

        std::memcpy(outDebugPixels, debugImg.data, procW * procH * 4);
    }

    return 1;
}

} // anonymous namespace

extern "C" {

/**
 * @brief オセロ盤面の認識と石の判定を一括で実行する C++ ネイティブ関数 (RGBA/BGRA入力)
 */
EXPORT_API int OthelloCv_RecognizeBoard(
    const unsigned char* srcPixels,
    int width,
    int height,
    int isBgra,
    int* outBoard,
    float* outCorners,
    unsigned char* outDebugPixels,
    int warpSize,
    int minAreaThreshold,
    int blackThreshold,
    int whiteThreshold,
    int transparentBg
) {
    if (!srcPixels || width <= 0 || height <= 0 || !outBoard || !outCorners) {
        return 0;
    }

    cv::Mat src(height, width, CV_8UC4, const_cast<unsigned char*>(srcPixels));
    cv::Mat bgr;
    if (isBgra) {
        cv::cvtColor(src, bgr, cv::COLOR_BGRA2BGR);
    } else {
        cv::cvtColor(src, bgr, cv::COLOR_RGBA2BGR);
    }

    return ProcessBgrImage(
        bgr, width, height,
        outBoard, outCorners, outDebugPixels,
        warpSize, minAreaThreshold, blackThreshold, whiteThreshold,
        transparentBg
    );
}

/**
 * @brief XREAL カメラの YUV420 プレーンを直接受け取り、OpenCV で認識および描画を行う
 */
EXPORT_API int OthelloCv_RecognizeBoardYUV(
    const unsigned char* yPlane,
    const unsigned char* uPlane,
    const unsigned char* vPlane,
    int width,
    int height,
    int* outBoard,
    float* outCorners,
    unsigned char* outDebugPixels,
    int outWidth,
    int outHeight,
    int warpSize,
    int minAreaThreshold,
    int blackThreshold,
    int whiteThreshold,
    int transparentBg
) {
    if (outWidth <= 0) outWidth = 640;
    if (outHeight <= 0) outHeight = 360;

    // 防御策: パラメータ不正時でも決して黒画面にせず、診断用ストライプを描画
    if (!yPlane || !uPlane || !vPlane || width <= 0 || height <= 0 || !outBoard || !outCorners) {
        if (outDebugPixels != nullptr) {
            for (int y = 0; y < outHeight; ++y) {
                for (int x = 0; x < outWidth; ++x) {
                    int idx = (y * outWidth + x) * 4;
                    bool stripe = ((x + y) / 16) % 2 == 0;
                    outDebugPixels[idx + 0] = stripe ? 0 : 20;    // R
                    outDebugPixels[idx + 1] = stripe ? 180 : 40;  // G
                    outDebugPixels[idx + 2] = stripe ? 220 : 90;  // B
                    outDebugPixels[idx + 3] = 255;                // A (100% Opaque)
                }
            }
        }
        return 0;
    }

    width = width & ~1;
    height = height & ~1;

    // I420 (YUV420p) バッファの組み立て
    int ySize = width * height;
    int uvWidth = width / 2;
    int uvHeight = height / 2;
    int uvSize = uvWidth * uvHeight;
    cv::Mat i420(height + uvHeight, width, CV_8UC1);

    std::memcpy(i420.data, yPlane, ySize);
    std::memcpy(i420.data + ySize, uPlane, uvSize);
    std::memcpy(i420.data + ySize + uvSize, vPlane, uvSize);

    cv::Mat bgrFull;
    cv::cvtColor(i420, bgrFull, cv::COLOR_YUV2BGR_I420);

    // 処理解像度 (outWidth x outHeight) へのリサイズ
    cv::Mat bgr;
    if (width != outWidth || height != outHeight) {
        cv::resize(bgrFull, bgr, cv::Size(outWidth, outHeight));
    } else {
        bgr = bgrFull;
    }

    int res = ProcessBgrImage(
        bgr, outWidth, outHeight,
        outBoard, outCorners, outDebugPixels,
        warpSize, minAreaThreshold, blackThreshold, whiteThreshold,
        transparentBg
    );

    // デバッグピクセルが有効かつ通常モードなら左上に OpenCV 稼働インジケータードット (緑) を付加
    if (outDebugPixels != nullptr && !transparentBg) {
        for (int dy = 4; dy < 14 && dy < outHeight; ++dy) {
            for (int dx = 4; dx < 14 && dx < outWidth; ++dx) {
                int p = (dy * outWidth + dx) * 4;
                outDebugPixels[p + 0] = 0;   // R
                outDebugPixels[p + 1] = 255; // G
                outDebugPixels[p + 2] = 100; // B
                outDebugPixels[p + 3] = 255; // A
            }
        }
    }

    return res;
}

/**
 * @brief 4点の対応関係から SolvePnP (3次元姿勢推定) を実行する
 * @param objPts 3Dローカル座標 (4点 x 3 = 12 floats: X, Y, Z)
 * @param imgPts 2D画像座標 (4点 x 2 = 8 floats: u, v)
 * @param fx カメラ焦点距離 X
 * @param fy カメラ焦点距離 Y
 * @param cx カメラ主点 X
 * @param cy カメラ主点 Y
 * @param outRvec 出力回転ベクトル (3 floats: Rodrigues形式)
 * @param outTvec 出力並進ベクトル (3 floats: tx, ty, tz)
 * @return 1: 成功, 0: 失敗
 */
EXPORT_API int OthelloCv_SolvePnP(
    const float* objPts,
    const float* imgPts,
    float fx, float fy, float cx, float cy,
    float* outRvec,
    float* outTvec
) {
    if (!objPts || !imgPts || !outRvec || !outTvec) return 0;

    std::vector<cv::Point3f> objectPoints(4);
    for (int i = 0; i < 4; ++i) {
        objectPoints[i] = cv::Point3f(objPts[i * 3 + 0], objPts[i * 3 + 1], objPts[i * 3 + 2]);
    }

    std::vector<cv::Point2f> imagePoints(4);
    for (int i = 0; i < 4; ++i) {
        imagePoints[i] = cv::Point2f(imgPts[i * 2 + 0], imgPts[i * 2 + 1]);
    }

    cv::Mat cameraMatrix = (cv::Mat_<double>(3, 3) <<
        fx, 0.0, cx,
        0.0, fy, cy,
        0.0, 0.0, 1.0
    );
    cv::Mat distCoeffs = cv::Mat::zeros(4, 1, CV_64F);

    cv::Mat rvec, tvec;
    // 平面正方形に最適な IPPE_SQUARE を優先
    bool success = cv::solvePnP(objectPoints, imagePoints, cameraMatrix, distCoeffs, rvec, tvec, false, cv::SOLVEPNP_IPPE_SQUARE);
    if (!success) {
        success = cv::solvePnP(objectPoints, imagePoints, cameraMatrix, distCoeffs, rvec, tvec, false, cv::SOLVEPNP_ITERATIVE);
    }

    if (success) {
        outRvec[0] = static_cast<float>(rvec.at<double>(0));
        outRvec[1] = static_cast<float>(rvec.at<double>(1));
        outRvec[2] = static_cast<float>(rvec.at<double>(2));

        outTvec[0] = static_cast<float>(tvec.at<double>(0));
        outTvec[1] = static_cast<float>(tvec.at<double>(1));
        outTvec[2] = static_cast<float>(tvec.at<double>(2));
        return 1;
    }
    return 0;
}

} // extern "C"
