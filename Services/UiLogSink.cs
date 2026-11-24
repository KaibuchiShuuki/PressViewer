using Serilog.Core;
using Serilog.Events;
using PressViewer.Models;
using PressViewer.Services.Interfaces;

namespace PressViewer.Services;

public class UiLogSink : ILogEventSink
{
    private readonly ILogBufferService _buffer;

    public UiLogSink(ILogBufferService buffer)
    {
        _buffer = buffer;
    }

    public void Emit(LogEvent logEvent)
    {
        var entry = new LogEntry
        {
            Timestamp = logEvent.Timestamp.DateTime,
            Level = logEvent.Level.ToString(),
            Message = logEvent.RenderMessage(),
            Exception = logEvent.Exception?.ToString(),
            SourceContext = logEvent.Properties.TryGetValue("SourceContext", out var sc)
                ? sc.ToString().Trim('"')
                : null,
        };

        _buffer.Add(entry);
    }
}
