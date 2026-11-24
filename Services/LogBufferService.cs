using System.Collections.ObjectModel;
using System.Windows.Data;
using PressViewer.Models;
using PressViewer.Services.Interfaces;

namespace PressViewer.Services;

public class LogBufferService : ILogBufferService
{
    private readonly object _lock = new();

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public LogBufferService()
    {
        // 允许跨线程操作 ObservableCollection
        BindingOperations.EnableCollectionSynchronization(LogEntries, _lock);
    }

    public void Add(LogEntry entry)
    {
        lock (_lock)
        {
            LogEntries.Add(entry);
        }
    }
}
