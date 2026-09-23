using SnappySnap.Core;
using Xunit;
namespace SnappySnap.Core.Tests;
public sealed class VideoEditSessionTests
{
    private static TimeSpan T(double seconds) => TimeSpan.FromSeconds(seconds);
    [Fact] public void DeleteAcrossFragmentsUsesEditedTimeAndPreservesSourceMapping()
    {
        var edit = new VideoEditSession(T(12)); edit.Delete(T(2), T(4)); edit.Delete(T(3), T(7));
        Assert.Equal(new[] { new TimeRange(T(0), T(2)), new TimeRange(T(4), T(5)), new TimeRange(T(9), T(12)) }, edit.Segments);
        Assert.Equal(T(6), edit.Duration); Assert.Equal(T(4), edit.ToSource(T(2))); Assert.Equal(T(9), edit.ToSource(T(3)));
        Assert.Equal(T(3), edit.ToEdited(T(8))); Assert.Equal(T(4), edit.ToEdited(T(10)));
        Assert.Equal(edit.Segments, VideoEditSession.KeepRanges(edit.ExportTimeline()));
    }
    [Fact] public void TrimUndoRedoAndNewEditDiscardRedo()
    {
        var edit = new VideoEditSession(T(10)); edit.Trim(T(1), T(9)); edit.Delete(T(2), T(4));
        edit.Undo(); Assert.Equal(T(8), edit.Duration); edit.Redo(); Assert.Equal(T(6), edit.Duration);
        edit.Undo(); edit.Trim(T(0), T(5)); Assert.False(edit.CanRedo); Assert.Equal(T(1), edit.ToSource(T(0))); Assert.Equal(T(6), edit.ToSource(T(5)));
    }
    [Theory] [InlineData(0, 2)] [InlineData(3, 6)] [InlineData(8, 10)]
    public void DeleteBeginningMiddleAndEnd(double start, double end)
    {
        var edit = new VideoEditSession(T(10)); edit.Delete(T(start), T(end)); Assert.Equal(T(10-end+start), edit.Duration);
        Assert.Equal(edit.Segments, VideoEditSession.KeepRanges(edit.ExportTimeline()));
    }
    [Fact] public void EmptyResultIsRejectedWithoutChangingUndoHistory()
    {
        var edit = new VideoEditSession(T(10)); Assert.Throws<InvalidOperationException>(() => edit.Delete(T(0), T(10)));
        Assert.Equal(T(10), edit.Duration); Assert.False(edit.CanUndo);
    }
    [Fact] public void RemovedRangesOutsideTrimDoNotDuplicateKeptFootage()
    {
        var timeline = new VideoEditTimeline(T(20), T(4), T(8), new[] { new TimeRange(T(0), T(2)), new TimeRange(T(8), T(10)), new TimeRange(T(15), T(19)) });
        Assert.Equal(new[] { new TimeRange(T(4), T(8)), new TimeRange(T(10), T(12)) }, VideoEditSession.KeepRanges(timeline));
    }
    [Fact] public void DecimalMegabytesAndSmallFilesAreExplicit()
    {
        Assert.EndsWith(" MB", FileSizeFormatter.Format(1_500_000)); Assert.StartsWith("<", FileSizeFormatter.Format(50));
        Assert.DoesNotContain("<", FileSizeFormatter.Format(0)); Assert.DoesNotContain("<", FileSizeFormatter.Format(100_000));
    }
}
