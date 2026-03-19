// Polyfill: enables C# 9 'init' accessors on netstandard2.1 / net472.
// See https://github.com/dotnet/runtime/issues/34978

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
