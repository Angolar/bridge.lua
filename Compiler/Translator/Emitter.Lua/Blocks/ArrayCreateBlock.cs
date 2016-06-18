using Bridge.Contract;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using System.Linq;

namespace Bridge.Translator.Lua
{
    public class ArrayCreateBlock : AbstractEmitterBlock
    {
        public ArrayCreateBlock(IEmitter emitter, ArrayCreateExpression arrayCreateExpression)
            : base(emitter, arrayCreateExpression)
        {
            this.Emitter = emitter;
            this.ArrayCreateExpression = arrayCreateExpression;
        }

        public ArrayCreateExpression ArrayCreateExpression
        {
            get;
            set;
        }

        protected override void DoEmit()
        {
            this.VisitArrayCreateExpression();
        }

        protected void VisitArrayCreateExpression()
        {
            ArrayCreateExpression arrayCreateExpression = this.ArrayCreateExpression;
            var rr = this.Emitter.Resolver.ResolveNode(arrayCreateExpression, this.Emitter) as ArrayCreateResolveResult;
            var at = (ArrayType)rr.Type;
            var rank = arrayCreateExpression.Arguments.Count;

            if (arrayCreateExpression.Initializer.IsNull && rank == 1)
            {
                string typedArrayName = null;
                if (this.Emitter.AssemblyInfo.UseTypedArrays && (typedArrayName = Helpers.GetTypedArrayName(at.ElementType)) != null)
                {
                    this.Write("new ", typedArrayName, "(");
                    arrayCreateExpression.Arguments.First().AcceptVisitor(this.Emitter);
                    this.Write(")");
                }
                else
                {
                    string typeName = BridgeTypes.ToJsName(at, this.Emitter);
                    this.Write(typeName);
                    this.WriteOpenParentheses();
                    arrayCreateExpression.Arguments.First().AcceptVisitor(this.Emitter);
                    this.WriteCloseParentheses();

                    /*
                    this.Write("Bridge.Array.init(");
                    arrayCreateExpression.Arguments.First().AcceptVisitor(this.Emitter);
                    this.WriteComma();

                    var def = Inspector.GetDefaultFieldValue(at.ElementType);
                    if (def == at.ElementType)
                    {
                        this.WriteFunction();
                        this.WriteOpenCloseParentheses();
                        this.BeginBlock();
                        this.WriteReturn(true);
                        this.Write(Inspector.GetStructDefaultValue(at.ElementType, this.Emitter));
                        this.WriteSemiColon();
                        this.WriteNewLine();
                        this.EndBlock();
                    }
                    else
                    {
                        this.WriteScript(def);
                    }

                    this.Write(")");*/
                }
                return;
            }

            /*
            if (at.Dimensions > 1)
            {
                this.Write("Bridge.Array.create(");
                var defaultInitializer = new PrimitiveExpression(Inspector.GetDefaultFieldValue(at.ElementType), "?");

                if (defaultInitializer.Value is IType)
                {
                    this.Write(Inspector.GetStructDefaultValue((IType)defaultInitializer.Value, this.Emitter));
                }
                else
                {
                    defaultInitializer.AcceptVisitor(this.Emitter);
                }

                this.WriteComma();
            }*/

            if (rr.InitializerElements != null && rr.InitializerElements.Count > 0)
            {
                if(at.Dimensions > 1) {
                    string typeName = BridgeTypes.ToJsName(((ArrayType)rr.Type).ElementType, this.Emitter);
                    this.Write("System.MultiArray");
                    this.WriteOpenParentheses();
                    this.Write(typeName);
                    this.WriteCloseParentheses();
                    this.WriteOpenParentheses();
                    this.Emitter.Comma = true;
                    this.WriteOpenBrace();
                    for(int i = 0; i < rr.SizeArguments.Count; i++) {
                        var a = rr.SizeArguments[i];
                        if(a.IsCompileTimeConstant) {
                            this.Write(a.ConstantValue);
                        }
                        else if(arrayCreateExpression.Arguments.Count > i) {
                            var arg = arrayCreateExpression.Arguments.ElementAt(i);

                            if(arg != null) {
                                arg.AcceptVisitor(this.Emitter);
                            }
                        }
                        if(i != rr.SizeArguments.Count - 1) {
                            this.EnsureComma(false);
                        }
                        this.Emitter.Comma = true;
                    }
                    this.WriteCloseBrace();
                    this.Emitter.Comma = false;
                    this.WriteComma();
                    var elements = arrayCreateExpression.Initializer.Elements;
                    new ExpressionListBlock(this.Emitter, elements, null).Emit();
                    this.WriteCloseParentheses();
                }
                else {
                    string typeName = BridgeTypes.ToJsName(((ArrayType)rr.Type).ElementType, this.Emitter);
                    this.Write("System.Array");
                    this.WriteOpenParentheses();
                    this.Write(typeName);
                    this.WriteCloseParentheses();
                    this.WriteOpenParentheses();
                    this.Write(rr.InitializerElements.Count);
                    this.WriteComma();
                    var elements = arrayCreateExpression.Initializer.Elements;
                    new ExpressionListBlock(this.Emitter, elements, null).Emit();
                    this.WriteCloseParentheses();
                }
            }
            else if (at.Dimensions > 1)
            {
                this.Write("null");
            }
            else
            {
                this.Write("[]");
            }

            /*
            if (at.Dimensions > 1)
            {
                this.Emitter.Comma = true;

                for (int i = 0; i < rr.SizeArguments.Count; i++)
                {
                    var a = rr.SizeArguments[i];
                    this.EnsureComma(false);

                    if (a.IsCompileTimeConstant)
                    {
                        this.Write(a.ConstantValue);
                    }
                    else if (arrayCreateExpression.Arguments.Count > i)
                    {
                        var arg = arrayCreateExpression.Arguments.ElementAt(i);

                        if (arg != null)
                        {
                            arg.AcceptVisitor(this.Emitter);
                        }
                    }
                    this.Emitter.Comma = true;
                }

                this.Write(")");
                this.Emitter.Comma = false;
            }
            */
        }
    }
}
