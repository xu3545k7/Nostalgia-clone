namespace Judgment
{
    public static class ScoreManager
    {
        public static int GetScore(JudgmentResult result, int combo, float perfectAcc)
        {
            switch (result)
            {
                case JudgmentResult.Perfect:
                    return (int)(1000000f / combo * perfectAcc);
                case JudgmentResult.Great:
                    return (int)(900000f / combo * perfectAcc);
                case JudgmentResult.Good:
                    return (int)(500000f / combo * perfectAcc);
                default:
                    return 0;
            }
        }
    }
}
