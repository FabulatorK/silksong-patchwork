// Polyfill required for 'init' property accessors and 'record' types
// when targeting netstandard2.1 with C# 9+. The compiler looks for this
// type in the System.Runtime.CompilerServices namespace; it is provided
// by the runtime in .NET 5+ but must be declared manually for older targets.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
