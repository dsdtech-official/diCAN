namespace DiCAN.Core.Serial;

// Classifies serial errors.
public static class SerialErrorClassifier
{

    // Classifies the input value.
    public static SerialErrorKind Classify(Exception? exception) => exception switch
    {
        null => SerialErrorKind.Unknown,

        UnauthorizedAccessException => SerialErrorKind.AccessDenied,

        ArgumentException => SerialErrorKind.PortNotFound,

        TimeoutException => SerialErrorKind.Timeout,

        ObjectDisposedException => SerialErrorKind.DeviceRemoved,

        FileNotFoundException => SerialErrorKind.PortNotFound,

        IOException => SerialErrorKind.DeviceRemoved,
        InvalidOperationException => SerialErrorKind.DeviceRemoved,

        _ => SerialErrorKind.Unknown,
    };

    // Classifies the input value.
    public static SerialErrorKind? ClassifyReadLoopStop(Exception exception, bool cancellationRequested)
    {
        if (cancellationRequested)
        {
            return null;
        }

        return exception is OperationCanceledException
            ? SerialErrorKind.DeviceRemoved
            : Classify(exception);
    }
}
