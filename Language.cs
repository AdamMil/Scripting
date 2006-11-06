using System;
using System.IO;
using Scripting.Runtime;

namespace Scripting.AST
{

public enum DecoratorType
{
  Compiled, Interpreted
}

/// <summary>The base class of all languages built on top of the scripting platform.</summary>
public abstract class Language
{
  protected Language(string name) { Name = name; }

  public virtual Type FunctionTemplateType
  {
    get { return typeof(FunctionTemplate); }
  }

  public virtual Type ParameterDictionaryType
  {
    get { return typeof(System.Collections.Hashtable); }
  }
  
  public virtual Type ParameterListType
  {
    get { return typeof(object[]); }
  }

  public readonly string Name;

  public virtual CompilerState CreateCompilerState()
  {
    return new CompilerState(this);
  }

  public abstract ASTDecorator CreateDecorator(DecoratorType type);

  public abstract IParser CreateParser(IScanner scanner);

  public abstract IScanner CreateScanner(params string[] sourceNames);
  public abstract IScanner CreateScanner(params TextReader[] sources);
  public abstract IScanner CreateScanner(TextReader[] sources, string[] sourceNames);

  public IScanner CreateScanner(TextReader source, string sourceName)
  {
    return CreateScanner(new TextReader[] { source }, new string[] { sourceName });
  }

  public void Decorate(ref ASTNode node, DecoratorType type)
  {
    CreateDecorator(type).Process(ref node);
  }

  public ASTNode Parse(TextReader source, string sourceName)
  {
    return Parse(CreateScanner(source, sourceName));
  }
  
  public ASTNode ParseFile(string filename)
  {
    return Parse(CreateScanner(new StreamReader(filename), filename));
  }

  public override string ToString()
  {
    return Name + " language";
  }
  
  ASTNode Parse(IScanner scanner)
  {
    IParser parser = CreateParser(scanner);
    try
    {
      return parser.ParseProgram();
    }
    catch(Exception e)
    {
      CompilerState.Current.Messages.Add(new OutputMessage(OutputMessageType.Error, e.Message));
      return null;
    }
  }
}

} // namespace Scripting.AST