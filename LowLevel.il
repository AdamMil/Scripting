.assembly extern mscorlib
{
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 )                         // .z\V.4..
  .ver 2:0:0:0
}
.assembly extern Scripting
{
  .ver 0:1:0:0
}

.module Scripting.LowLevel.netmodule
.imagebase 0x00300000
.corflags 0x00000001    //  ILONLY

.namespace Scripting.Runtime
{

.class public auto ansi sealed LowLevel
{
  .method public static hidebysig float32 FloatRemainder(float32 a, float32 b) cil managed
  {
    .maxstack 2
    ldarg.0 // return a rem b
    ldarg.1
    rem
    ret
  }

  .method public static hidebysig float64 DoubleRemainder(float64 a, float64 b) cil managed
  {
    .maxstack 2
    ldarg.0 // return a rem b
    ldarg.1
    rem
    ret
  }
}
  
} // .namespace Scripting.Runtime