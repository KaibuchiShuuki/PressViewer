using System;

namespace PressViewer.Models;

public class KeyPressRecord
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public DateTime PressTime { get; set; }
}
