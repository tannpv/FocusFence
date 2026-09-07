namespace FocusFence.Core;

public interface IStartupRegistration
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}
