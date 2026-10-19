namespace PokerSettle.Core.Diagnostics;

/// <summary>
/// Detects a crash in the previous session with an empty marker file (it holds no details).
/// The marker exists while the app is in the foreground and is removed when the app goes to the
/// background or closes normally. If it's still there at the next launch, the app died in the foreground.
/// </summary>
public sealed class CrashMarker(string directory)
{
    public string MarkerPath { get; } = Path.Combine(directory, "session.marker");

    /// <summary>Set once by <see cref="OnStartup"/>.</summary>
    public bool PreviousSessionCrashed { get; private set; }

    public void OnStartup()
    {
        PreviousSessionCrashed = File.Exists(MarkerPath);
        MarkActive();
    }

    public void MarkActive()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            if (!File.Exists(MarkerPath)) File.WriteAllBytes(MarkerPath, []);
        }
        catch (IOException)
        {
            // Best effort only.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void MarkInactive()
    {
        try
        {
            File.Delete(MarkerPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
