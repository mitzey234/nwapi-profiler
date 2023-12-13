namespace CustomProfiler.API;

using System.Runtime.CompilerServices;

public unsafe readonly struct AsRef<T>
{
    private readonly void* ptr;

    public AsRef(ref T reference)
    {
        ptr = Unsafe.AsPointer(ref reference);
    }

    public ref T Value
    {
        get
        {
            return ref Unsafe.AsRef<T>(ptr);
        }
    }
}
