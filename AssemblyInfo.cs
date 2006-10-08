using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Scripting Runtime")]
[assembly: AssemblyDescription("A platform for building dynamic languages that target the .NET runtime.")]
#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif
[assembly: AssemblyProduct("Scripting Runtime")]
[assembly: AssemblyCopyright("Copyright 2006, Adam Milazzo")]

[assembly: AssemblyVersion("0.1.0.0")]

[assembly: ComVisible(false)]