namespace DiCAN.Core.Abstractions;

// Manages i app.
public interface IAppPaths
{

    string UserDataDatabasePath { get; }

    string RecordingsFolder { get; }
}
