using System;

namespace PressViewer.Services.Interfaces;

public interface IGamepadHookService : IDisposable
{
    bool IsRunning { get; }

    event Action<string>? GamepadPressed;

    void Start();

    void Stop();
}
