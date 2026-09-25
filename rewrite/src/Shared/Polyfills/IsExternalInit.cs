#if NETSTANDARD2_1
namespace System.Runtime.CompilerServices
{
    /// <summary>Lets C# 9 records and init-only setters compile for netstandard2.1 (Unity).</summary>
    internal static class IsExternalInit { }
}
#endif
