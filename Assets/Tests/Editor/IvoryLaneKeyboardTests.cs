using NUnit.Framework;

public class IvoryLaneKeyboardTests
{
    [TestCase(4, 6, 5)]
    [TestCase(4, 5, 5)]
    [TestCase(5, 4, 5)]
    [TestCase(0, 0, 0)]
    [TestCase(26, 30, 27)]
    public void ResolveDebugLane_UsesMiddleOrRightLane(int start, int end, int expected)
    {
        Assert.AreEqual(expected, IvoryLaneKeyboard.ResolveDebugLane(start, end));
    }
}
