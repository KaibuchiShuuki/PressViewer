using System.Collections.ObjectModel;
using PressViewer.Models;

namespace PressViewer.Services.Interfaces;

public interface ILogBufferService
{
    ObservableCollection<LogEntry> LogEntries { get; }
    void Add(LogEntry entry);
}
