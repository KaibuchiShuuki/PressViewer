using System.Collections.Generic;
using PressViewer.Models;

namespace PressViewer.Services.Interfaces;

public interface IKeyDatabaseService
{
    void SaveKeyPress(KeyPressRecord record);
    Dictionary<string, int> GetKeyCounts();
    Dictionary<string, int> GetKeyCounts(DateTime? from, DateTime? to);
}
