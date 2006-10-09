using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;
using System.Reflection;
using System.Reflection.Emit;
using Scripting.AST;

namespace Scripting.Emit
{

public sealed class AssemblyGenerator
{
  public AssemblyGenerator(string moduleName) : this(moduleName, false) { }
  public AssemblyGenerator(string moduleName, bool debug) : this(AppDomain.CurrentDomain, moduleName, debug) { }
  public AssemblyGenerator(string moduleName, string outFileName, bool debug)
    : this(AppDomain.CurrentDomain, moduleName, moduleName, outFileName, debug) { }
  public AssemblyGenerator(AppDomain domain, string moduleName, bool debug)
    : this(domain, moduleName, moduleName, RandomOutputFile(), debug) { }
  public AssemblyGenerator(AppDomain domain, string assemblyName, string moduleName, string outFileName, bool debug)
  {
    if(domain == null || assemblyName == null || moduleName == null || outFileName == null)
    {
      throw new ArgumentNullException();
    }

    if(assemblyName == string.Empty || moduleName == string.Empty || outFileName == string.Empty)
    {
      throw new ArgumentException("Assembly name, module name, and output file must be specified.");
    }

    string outDirectory = System.IO.Path.GetDirectoryName(outFileName);
    if(outDirectory == string.Empty)
    {
      outDirectory = null;
    }
    else if(!System.IO.Directory.Exists(outDirectory))
    {
      throw new System.IO.DirectoryNotFoundException("Output directory not found: "+outDirectory);
    }

    OutFileName = System.IO.Path.GetFileName(outFileName);
    if(string.IsNullOrEmpty(OutFileName))
    {
      throw new ArgumentException("Output file name must be specified.");
    }

    AssemblyName an = new AssemblyName();
    an.Name = assemblyName;
    
    Assembly = domain.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndSave, outDirectory,
                                            null, null, null, null, true);
  
    IsDebug = debug;
    // if debugging is enabled, add an assembly attribute to disable JIT optimizations and track sequence points
    if(debug)
    {
      ConstructorInfo ci =
        typeof(DebuggableAttribute).GetConstructor(new Type[] { typeof(DebuggableAttribute.DebuggingModes) });
      CustomAttributeBuilder ab = new CustomAttributeBuilder(ci,
        new object[] { DebuggableAttribute.DebuggingModes.DisableOptimizations |
                       DebuggableAttribute.DebuggingModes.Default });
      Assembly.SetCustomAttribute(ab);
    }
    
    Module = Assembly.DefineDynamicModule(OutFileName, OutFileName, debug);
  }
  
  public readonly AssemblyBuilder Assembly;
  public readonly ModuleBuilder Module;
  public readonly string OutFileName;
  public readonly bool IsDebug;
  public ISymbolDocumentWriter Symbols;
  
  #region DefineType
  /// <summary>Defines a new public type in the assembly.</summary>
  public TypeGenerator DefineType(string name)
  {
    return DefineType(TypeAttributes.Public, name, (ITypeInfo)null);
  }

  /// <summary>Defines a new public type in the assembly.</summary>
  public TypeGenerator DefineType(string name, Type baseType)
  {
    return DefineType(name, baseType == null ? null : new TypeWrapper(baseType));
  }

  /// <summary>Defines a new public type in the assembly.</summary>
  public TypeGenerator DefineType(string name, ITypeInfo baseType)
  {
    return DefineType(TypeAttributes.Public, name, baseType);
  }

  /// <summary>Defines a new type in the assembly.</summary>
  public TypeGenerator DefineType(TypeAttributes attributes, string name)
  {
    return DefineType(attributes, name, (ITypeInfo)null);
  }

  /// <summary>Defines a new type in the assembly.</summary>
  public TypeGenerator DefineType(TypeAttributes attributes, string name, Type baseType)
  {
    return DefineType(attributes, name, baseType == null ? null : new TypeWrapper(baseType));
  }

  /// <summary>Defines a new type in the assembly.</summary>
  public TypeGenerator DefineType(TypeAttributes attributes, string name, ITypeInfo baseType)
  {
    TypeBuilder builder = Module.DefineType(name, attributes, baseType == null ? null : baseType.DotNetType);
    TypeGenerator tg = new TypeGenerator(this, builder, baseType, null);
    types.Add(tg);
    return tg;
  }
  #endregion

  /// <summary>Finalizes all types in the assembly and all global methods and data.</summary>
  public void Finish()
  {
    foreach(TypeGenerator tg in types)
    {
      tg.FinishType();
    }

    Module.CreateGlobalFunctions();
  }

  /// <summary>Creates an <see cref="ICallable"/> type that will invoke the given method. If the method is not static
  /// and is not a constructor, the 'this' pointer must be passed as the first argument when calling the wrapper.
  /// </summary>
  public ITypeInfo GetMethodWrapper(IMethodBase method)
  {
    Signature signature = new Signature(method);
    ITypeInfo wrapperType;

    if(!methodWrappers.TryGetValue(signature, out wrapperType))
    {
      methodWrappers[signature] = wrapperType =
        DotNetInterop.MakeMethodWrapper(GetPrivateClass(), signature, method, "mwrap$" + wrapperIndex.Next);
    }
    
    return wrapperType;
  }

  /// <summary>Gets a <see cref="TypeGenerator"/> for private implementation details.</summary>
  public TypeGenerator GetPrivateClass()
  {
    if(privateClass == null)
    {
      privateClass = DefineType(TypeAttributes.NotPublic, "<Private Implementation Details>");
      privateClass.MarkAsNonUserCode();
    }
    return privateClass;
  }

  /// <summary>Saves the assembly to the path given in the constructor.</summary>
  public void Save()
  {
    Assembly.Save(OutFileName);
  }

  readonly Dictionary<Signature,ITypeInfo> methodWrappers = new Dictionary<Signature,ITypeInfo>();
  readonly List<TypeGenerator> types = new List<TypeGenerator>();
  readonly Index wrapperIndex = new Index();
  TypeGenerator privateClass;

  static string RandomOutputFile()
  {
    return System.IO.Path.GetTempFileName();
  }
}

} // namespace Scripting.Emit