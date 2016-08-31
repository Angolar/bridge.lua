using Bridge.Contract;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;

namespace Bridge.Translator.Lua
{
    public class NameBlock : AbstractEmitterBlock
    {
        public NameBlock(IEmitter emitter, NamedExpression namedExpression)
            : this(emitter, namedExpression.Name, namedExpression, namedExpression.Expression)
        {
        }

        public NameBlock(IEmitter emitter, string name, Expression namedExpression, Expression expression)
            : base(emitter, null)
        {
            this.Emitter = emitter;
            this.NamedExpression = namedExpression;
            this.Expression = expression;
            this.Name = name;

            this.Emitter.Translator.EmitNode = namedExpression ?? expression;
        }

        public string Name
        {
            get;
            set;
        }

        public Expression Expression
        {
            get;
            set;
        }

        public Expression NamedExpression
        {
            get;
            set;
        }

        protected override void DoEmit()
        {
            this.EmitNameExpression(this.Name, this.NamedExpression, this.Expression);
        }

        protected virtual void EmitNameExpression(string name, Expression namedExpression, Expression expression)
        {
            var resolveResult = this.Emitter.Resolver.ResolveNode(namedExpression, this.Emitter);

            if (!this.Emitter.AssemblyInfo.PreserveMemberCase)
            {
                name = Object.Net.Utilities.StringUtils.ToLowerCamelCase(name);
            }

            bool isSetProperty = false;
            if (resolveResult != null && resolveResult is MemberResolveResult)
            {
                string varName = LuaHelper.MergeVar;
                CheckConflictName(ref varName);
                this.Write(varName);
                var member = ((MemberResolveResult)resolveResult).Member;
                var preserveCase = !this.Emitter.IsNativeMember(member.FullName) ? this.Emitter.AssemblyInfo.PreserveMemberCase : false;
                name = this.Emitter.GetEntityName(member, preserveCase);

                var isProperty = member.SymbolKind == SymbolKind.Property;

                if (!isProperty)
                {
                    this.WriteDot();
                    this.Write(name);
                }
                else
                {
                    name = isProperty ? Helpers.GetPropertyRef(member, this.Emitter, !(expression is ArrayInitializerExpression)) : name;
                    if(Helpers.IsFieldProperty(member, this.Emitter)) {
                        this.WriteDot();
                        this.Write(name);
                    }
                    else {
                        this.WriteObjectColon();
                        this.Write(name);
                        this.WriteOpenParentheses();
                        isSetProperty = true;
                    }
                }
            }
            else
            {
                this.Write(name);
            }

            if(!isSetProperty) {
                this.WriteEqualsSign();
            }        
            expression.AcceptVisitor(this.Emitter);
            if(isSetProperty) {
                this.WriteCloseParentheses();
            }
        }
    }
}
