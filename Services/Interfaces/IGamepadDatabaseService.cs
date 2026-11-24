using PressViewer.Models;

namespace PressViewer.Services.Interfaces;

public interface IGamepadDatabaseService
{
    void SavePress(GamepadPressRecord record);

    Dictionary<string, int> GetPressCounts();

    Dictionary<string, int> GetPressCounts(DateTime? from, DateTime? to);
}
