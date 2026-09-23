namespace SnappySnap.Editor;

public enum EditorSaveMode { Replace, NewCopy, File }

public sealed record EditorSaveRequest(string Format, string? DestinationPath = null, EditorSaveMode Mode = EditorSaveMode.Replace);
