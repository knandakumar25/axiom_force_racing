namespace DriftCircuit
{
    /// <summary>Places both karts when the player finishes. The clocks share a fixed-step origin.</summary>
    internal readonly struct QuickRaceResult
    {
        public readonly double PlayerTime;
        public readonly double? RivalTime;
        public int PlayerPlace => RivalTime < PlayerTime ? 2 : 1;
        public int RivalPlace => RivalTime <= PlayerTime ? 1 : 2;

        public QuickRaceResult(double playerTime, double? rivalTime)
        {
            PlayerTime = playerTime;
            RivalTime = rivalTime;
        }
    }
}
