namespace ShowUp.Services;

public interface IDisplayRequestManager
{
    IDisposable RequestActive();

    void Clear();
}
