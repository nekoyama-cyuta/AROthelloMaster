namespace AROthello.Experiment
{
    /// <summary>フレーム時間を積算して平均 FPS (= フレーム数 / 経過時間) を求める。</summary>
    public class FrameRateAccumulator
    {
        private int _frames;
        private double _seconds;

        public int FrameCount => _frames;
        public double TotalSeconds => _seconds;

        /// <summary>平均 FPS。フレームが無ければ 0。</summary>
        public double AverageFps => _seconds > 0 ? _frames / _seconds : 0;

        /// <summary>1 フレーム分を加える。0 以下の時間は無視する。</summary>
        public void AddFrame(double deltaSeconds)
        {
            if (deltaSeconds <= 0 || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds)) return;
            _frames++;
            _seconds += deltaSeconds;
        }

        public void Reset()
        {
            _frames = 0;
            _seconds = 0;
        }
    }
}
