namespace UnityHawk {
internal interface ISharedBuffer {
    public void Open();

    public bool IsOpen();

    public void Close();
}
}