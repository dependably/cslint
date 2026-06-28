public class OutRefBoolSample
{
    // `out bool` is not a flag argument — OP005 must NOT fire here.
    public bool TryGet(string key, out bool found)
    {
        found = key.Length > 0;
        return found;
    }
}
