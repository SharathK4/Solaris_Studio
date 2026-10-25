namespace Solaris.Screens
{
    /// <summary>Outcome of a performance, shown on the results card.</summary>
    public sealed class StageResult
    {
        public string SongTitle;
        public int Strums, Hits, BestStreak;
        public float Accuracy => Strums > 0 ? (float)Hits / Strums : 0f;

        public string Grade
        {
            get
            {
                float a = Accuracy;
                return a >= 0.95f ? "S" : a >= 0.85f ? "A" : a >= 0.7f ? "B" : a >= 0.5f ? "C" : "D";
            }
        }
    }
}
