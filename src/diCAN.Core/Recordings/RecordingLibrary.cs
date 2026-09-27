namespace DiCAN.Core.Recordings;

// Manages recording volume.
public enum RecordingVolumeProblem
{

    Damaged,

    WrittenByNewerBuild,

    Unreadable,
}

// Manages unreadable recording.
public sealed record UnreadableRecordingVolume(
    string VolumeKey, RecordingVolumeProblem Problem, Exception Error);

// Stores recording library data.
public sealed record RecordingLibrary(
    IReadOnlyList<Recording> Recordings, IReadOnlyList<UnreadableRecordingVolume> UnreadableVolumes);
