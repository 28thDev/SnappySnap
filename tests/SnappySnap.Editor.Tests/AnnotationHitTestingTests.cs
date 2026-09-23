using System.Windows;
using SnappySnap.Editor;
using Xunit;
namespace SnappySnap.Editor.Tests;
public sealed class AnnotationHitTestingTests
{
    [Theory] [InlineData(.25)] [InlineData(.5)] [InlineData(1)] [InlineData(2)]
    public void LineTargetIsEightScreenDipsAtEveryZoom(double zoom)
    {
        var line = new LineElement(new Point(0, 100), new Point(400, 100));
        Assert.True(AnnotationHitTesting.Contains(line, new Point(200, 100 + 7.9 / zoom), zoom));
        Assert.False(AnnotationHitTesting.Contains(line, new Point(200, 100 + 8.1 / zoom), zoom));
    }
    [Fact] public void RectangleInteriorCanBeDraggedAndDiagonalLineDoesNotSelectItsEmptyBoundingBox()
    {
        Assert.True(AnnotationHitTesting.Contains(new RectangleElement(new Rect(0, 0, 400, 400)), new Point(200, 200), 1));
        Assert.False(AnnotationHitTesting.Contains(new LineElement(new Point(0, 0), new Point(400, 400)), new Point(50, 350), 1));
    }
}
