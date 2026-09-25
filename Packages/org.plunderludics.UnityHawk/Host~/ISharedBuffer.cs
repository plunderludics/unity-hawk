namespace UnityHawk.Host {

public interface ISharedBuffer {
    void Open();
    bool IsOpen();
    void Close();
}

}
