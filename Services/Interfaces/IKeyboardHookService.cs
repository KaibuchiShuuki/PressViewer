using System;

namespace PressViewer.Services.Interfaces;

public interface IKeyboardHookService
{
    event Action<string>? KeyPressed;
    void Start();
    void Stop();
}
