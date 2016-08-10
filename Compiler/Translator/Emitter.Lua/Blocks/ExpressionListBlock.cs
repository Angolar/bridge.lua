using Bridge.Contract;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.TypeSystem;
using System.Collections.Generic;
using System.Linq;

namespace Bridge.Translator.Lua
{
    public class ExpressionListBlock : AbstractEmitterBlock
    {
        public ExpressionListBlock(IEmitter emitter, IEnumerable<Expression> expressions, Expression paramArg, InvocationExpression invocation = null)
            : base(emitter, null)
        {
            this.Emitter = emitter;
            this.Expressions = expressions;
            this.ParamExpression = paramArg;
            this.InvocationExpression = invocation;
        }

        public IEnumerable<Expression> Expressions
        {
            get;
            set;
        }

        public Expression ParamExpression
        {
            get;
            set;
        }

        public InvocationExpression InvocationExpression
        {
            get;
            set;
        }

        protected override void DoEmit()
        {
            var oldIsAssignment = this.Emitter.IsAssignment;
            var oldUnary = this.Emitter.IsUnaryAccessor;
            this.Emitter.IsAssignment = false;
            this.Emitter.IsUnaryAccessor = false;
            this.EmitExpressionList(this.Expressions, this.ParamExpression);
            this.Emitter.IsAssignment = oldIsAssignment;
            this.Emitter.IsUnaryAccessor = oldUnary;
        }

        protected virtual void EmitExpressionList(IEnumerable<Expression> expressions, Expression paramArg)
        {
            bool needComma = false;
            int count = this.Emitter.Writers.Count;
            bool expanded = true;
            IType paramType = null;

            if (paramArg != null && this.InvocationExpression != null)
            {
                var rr = this.Emitter.Resolver.ResolveNode(this.InvocationExpression, this.Emitter) as CSharpInvocationResolveResult;
                if (rr != null)
                {
                    expanded = rr.IsExpandedForm;
                    if(rr.IsExpandedForm) {
                        var argResolveResult = Emitter.Resolver.ResolveNode(paramArg, Emitter);
                        var type = rr.Member.Parameters.Last().Type;
                        if(argResolveResult.Type != type) {
                            paramType = type;
                        }
                        else {
                            paramArg = null;
                        }
                    }
                }
            }

            int index = 0;
            foreach (var expr in expressions)
            {
                if (expr != null)
                {
                    this.Emitter.Translator.EmitNode = expr;
                    if(needComma) {
                        this.WriteComma();
                    }

                    if(expanded && expr == paramArg) {
                        string typeName = BridgeTypes.ToJsName(paramType, this.Emitter);
                        this.Write(typeName);
                        this.WriteOpenParentheses();
                        this.Write(expressions.Count() - index);
                        this.WriteComma();
                        this.WriteSpace();
                    }

                    needComma = true;
                    int pos = this.Emitter.Output.Length;
                    expr.AcceptVisitor(this.Emitter);

                    if(this.Emitter.Writers.Count != count) {
                        this.PopWriter();
                        count = this.Emitter.Writers.Count;
                    }

                    if(expr is AssignmentExpression) {
                        Helpers.CheckValueTypeClone(this.Emitter.Resolver.ResolveNode(expr, this.Emitter), expr, this, pos);
                    }
                }
                ++index;
            }

            if (expanded && paramArg != null)
            {
                this.WriteCloseParentheses();
            }
        }
    }
}