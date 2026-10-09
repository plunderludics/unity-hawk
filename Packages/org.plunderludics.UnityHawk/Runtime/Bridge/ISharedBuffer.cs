namespace Plunderludics.BizHawk.Bridge {

public interface ISharedBuffer {
    void Open();
    bool IsOpen();
    void Close();
}

}
