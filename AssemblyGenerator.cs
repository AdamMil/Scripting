using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;
using System.Reflection;
using System.Reflection.Emit;
using Scripting.AST;

namespace Scripting.Emit
{

public abstract class Snippet
{
  public abstract object Run();
}

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
    return DefineType(name, baseType == null ? null : TypeWrapper.Get(baseType));
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
    return DefineType(attributes, name, baseType == null ? null : TypeWrapper.Get(baseType));
  }

  /// <summary>Defines a new type in the assembly.</summary>
  public TypeGenerator DefineType(TypeAttributes attributes, string name, ITypeInfo baseType)
  {
    TypeBuilder builder = Module.DefineType(name, attributes, baseType == null ? null : baseType.DotNetType);
    TypeGenerator tg = new TypeGenerator(this, builder, baseType, null);
    privates.Types.Add(tg);
    return tg;
  }
  #endregion

  /// <summary>Finalizes all types in the assembly and all global methods and data.</summary>
  public void Finish()
  {
    if(privates.Previous != null) throw new InvalidOperationException();

    foreach(TypeGenerator tg in privates.Types)
    {
      tg.FinishType();
    }

    Module.CreateGlobalFunctions();
  }

  public Snippet GenerateSnippet(ASTNode body)
  {
    if(body == null) throw new ArgumentNullException();

    privates = new Privates(privates);
    try
    {
      TypeGenerator tg = DefineType(TypeAttributes.Public|TypeAttributes.Sealed, "snippet$"+snippetIndex.Next,
                                    typeof(Snippet));
      privates.PrivateClass = tg; // we'll use the snippet itself as the private class

      CodeGenerator cg = tg.DefineMethodOverride("Run");
      cg.EmitTypedNode(body, TypeWrapper.Object);
      cg.Finish();

      foreach(TypeGenerator type in privates.Types) type.FinishType();

      return (Snippet)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
    }
    finally { privates = privates.Previous; }
  }

  /// <summary>Creates an <see cref="ICallable"/> type that will invoke the given method. If the method is not static
  /// and is not a constructor, the 'this' pointer must be passed as the first argument when calling the wrapper.
  /// </summary>
  public ITypeInfo GetMethodWrapper(IMethodBase method)
  {
    Signature signature = new Signature(method);
    ITypeInfo wrapperType;

    if(privates.MethodWrappers == null)
    {
      privates.MethodWrappers = new Dictionary<Signature, ITypeInfo>();
    }

    if(!privates.MethodWrappers.TryGetValue(signature, out wrapperType))
    {
      privates.MethodWrappers[signature] = wrapperType =
        DotNetInterop.MakeMethodWrapper(GetPrivateClass(), signature, method, "mwrap$" + wrapperIndex.Next);
    }
    
    return wrapperType;
  }

  /// <summary>Gets a <see cref="TypeGenerator"/> for private implementation details.</summary>
  public TypeGenerator GetPrivateClass()
  {
    if(privates.PrivateClass == null)
    {
      privates.PrivateClass = DefineType(TypeAttributes.NotPublic, "<Private Implementation Details>");
      privates.PrivateClass.MarkAsNonUserCode();
    }
    return privates.PrivateClass;
  }

  /// <summary>Saves the assembly to the path given in the constructor.</summary>
  public void Save()
  {
    Assembly.Save(OutFileName);
  }

  /// <summary>Gets whether the assembly is currently generating a snippet.</summary>
  /// <remarks>This is used to determine, for instance, whether constant module data can be emitted.</remarks>
  internal bool IsCreatingSnippet
  {
    get { return privates.Previous != null; }
  }

  sealed class Privates
  {
    public Privates(Privates previous) { Previous = previous; }

    public Dictionary<Signature, ITypeInfo> MethodWrappers;
    public TypeGenerator PrivateClass;
    public readonly List<TypeGenerator> Types = new List<TypeGenerator>();
    public readonly Privates Previous;
  }

  Privates privates = new Privates(null);
  readonly Index wrapperIndex = new Index(), snippetIndex = new Index();

  static string RandomOutputFile()
  {
    return System.IO.Path.GetTempFileName();
  }
}

} // namespace Scripting.Emit