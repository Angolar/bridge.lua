using Bridge.Contract;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using System;
using System.Linq;
using System.Text;

namespace Bridge.Translator.Lua
{
    public class InvocationBlock : ConversionBlock
    {
        public InvocationBlock(IEmitter emitter, InvocationExpression invocationExpression)
            : base(emitter, invocationExpression)
        {
            this.Emitter = emitter;
            this.InvocationExpression = invocationExpression;
        }

        public InvocationExpression InvocationExpression
        {
            get;
            set;
        }

        protected override Expression GetExpression()
        {
            return this.InvocationExpression;
        }

        private bool CheckHasRefArgs(out string returnString, out string returnVarName) {
            returnString = returnVarName = null;

            InvocationExpression invocationExpression = this.InvocationExpression;
            Tuple<bool, bool, string> inlineInfo = this.Emitter.GetInlineCode(invocationExpression);
            var argsInfo = new ArgumentsInfo(this.Emitter, invocationExpression);
            var argsExpressions = argsInfo.ArgumentsExpressions;

            StringBuilder sb = new StringBuilder();
            bool isFirst = true;
            foreach(var item in argsExpressions) {
                DirectionExpression itemExp = item as DirectionExpression;
                if(itemExp != null) {
                    if(itemExp.FieldDirection == FieldDirection.Out || itemExp.FieldDirection == FieldDirection.Ref) {
                        string name = itemExp.Expression.ToString();
                        string outName;
                        if(this.Emitter.LocalsNamesMap.TryGetValue(name, out outName)) {
                            name = outName;
                        }
                        if(isFirst) {
                            isFirst = false;
                        }
                        else {
                            sb.Append(", ");
                        }
                        sb.Append(name);
                    }
                }
            }

            if(sb.Length > 0) {
                var resolveResult = (CSharpInvocationResolveResult)this.Emitter.Resolver.ResolveNode(invocationExpression, this.Emitter);
                if(resolveResult.Member.ReturnType.Kind != TypeKind.Void) {
                    if(invocationExpression.IsSingleExpression()) {
                        sb.Insert(0, "_, ");
                    }
                    else {
                        var iteratorVar = this.GetTempVarName();
                        var iteratorName = this.AddLocal(iteratorVar, AstType.Null);
                        sb.Insert(0, iteratorName + ", ");
                        returnVarName = iteratorName;
                    }
                }
                sb.Append(" = ");
                returnString = sb.ToString();
                return true;
            }
            return false;
        }

        protected override void EmitConversionExpression()
        {
            string returnString, returnVarName;
            bool hasRefs = CheckHasRefArgs(out returnString, out returnVarName);
            if(hasRefs) {
                if(returnVarName != null) {
                    this.PushWriter("{0}");
                    this.VisitInvocationExpression();
                    string script = this.PopWriter(true);
                    this.WriteToPrevLine(returnString + script);
                    this.Write(returnVarName);
                }
                else {
                    this.Write(returnString);
                    this.VisitInvocationExpression();
                }
            }
            else {
                this.VisitInvocationExpression();
            }
        }

        protected virtual bool IsEmptyPartialInvoking(IMethod method)
        {
            return method != null && method.IsPartial && !method.HasBody;
        }

        protected void WriteThisExtension(Expression target)
        {
            if (target.HasChildren)
            {
                var first = target.Children.ElementAt(0);
                var expression = first as Expression;

                if (expression != null)
                {
                    expression.AcceptVisitor(this.Emitter);
                }
            }
        }

        protected void VisitInvocationExpression()
        {
            InvocationExpression invocationExpression = this.InvocationExpression;
            int pos = this.Emitter.Output.Length;

            if (this.Emitter.IsForbiddenInvocation(invocationExpression))
            {
                throw new EmitterException(invocationExpression, "This method cannot be invoked directly");
            }

            var oldValue = this.Emitter.ReplaceAwaiterByVar;
            var oldAsyncExpressionHandling = this.Emitter.AsyncExpressionHandling;

            if (this.Emitter.IsAsync && !this.Emitter.AsyncExpressionHandling)
            {
                this.WriteAwaiters(invocationExpression);
                this.Emitter.ReplaceAwaiterByVar = true;
                this.Emitter.AsyncExpressionHandling = true;
            }

            Tuple<bool, bool, string> inlineInfo = this.Emitter.GetInlineCode(invocationExpression);
            var argsInfo = new ArgumentsInfo(this.Emitter, invocationExpression);

            var argsExpressions = argsInfo.ArgumentsExpressions;
            var paramsArg = argsInfo.ParamsExpression;
            var argsCount = argsExpressions.Count();

            if (inlineInfo != null)
            {
                bool isStaticMethod = inlineInfo.Item1;
                bool isInlineMethod = inlineInfo.Item2;
                string inlineScript = inlineInfo.Item3;

                if (isInlineMethod)
                {
                    if (invocationExpression.Arguments.Count > 0)
                    {
                        var code = invocationExpression.Arguments.First();
                        var inlineExpression = code as PrimitiveExpression;

                        if (inlineExpression == null)
                        {
                            throw new EmitterException(invocationExpression, "Only primitive expression can be inlined");
                        }

                        string value = inlineExpression.Value.ToString().Trim();

                        if (value.Length > 0)
                        {
                            value = InlineArgumentsBlock.ReplaceInlineArgs(this, inlineExpression.Value.ToString(), invocationExpression.Arguments.Skip(1).ToArray());
                            this.Write(value);

                            if (value[value.Length - 1] == ';')
                            {
                                this.Emitter.EnableSemicolon = false;
                                this.WriteNewLine();
                            }
                        }
                        else
                        {
                            // Empty string, emit nothing.
                            this.Emitter.EnableSemicolon = false;
                        }

                        this.Emitter.ReplaceAwaiterByVar = oldValue;
                        this.Emitter.AsyncExpressionHandling = oldAsyncExpressionHandling;

                        return;
                    }
                }
                else
                {
                    MemberReferenceExpression targetMemberRef = invocationExpression.Target as MemberReferenceExpression;
                    bool isBase = targetMemberRef != null && targetMemberRef.Target is BaseReferenceExpression;

                    if (!String.IsNullOrEmpty(inlineScript) && (isBase || invocationExpression.Target is IdentifierExpression))
                    {
                        argsInfo.ThisArgument = "this";
                        bool noThis = !inlineScript.Contains("{this}");

                        if (!isStaticMethod && noThis)
                        {
                            this.WriteThis();
                            this.WriteDot();
                        }

                        new InlineArgumentsBlock(this.Emitter, argsInfo, inlineScript).Emit();
                        this.Emitter.ReplaceAwaiterByVar = oldValue;
                        this.Emitter.AsyncExpressionHandling = oldAsyncExpressionHandling;

                        return;
                    }
                }
            }

            MemberReferenceExpression targetMember = invocationExpression.Target as MemberReferenceExpression;
            ResolveResult targetMemberResolveResult = null;
            if (targetMember != null)
            {
                targetMemberResolveResult = this.Emitter.Resolver.ResolveNode(targetMember.Target, this.Emitter);
            }

            if (targetMember != null)
            {
                var member = this.Emitter.Resolver.ResolveNode(targetMember.Target, this.Emitter);

                //var targetResolve = this.Emitter.Resolver.ResolveNode(targetMember, this.Emitter);
                var targetResolve = this.Emitter.Resolver.ResolveNode(invocationExpression, this.Emitter);

                if (targetResolve != null)
                {
                    var csharpInvocation = targetResolve as CSharpInvocationResolveResult;

                    InvocationResolveResult invocationResult;
                    bool isExtensionMethodInvocation = false;
                    if (csharpInvocation != null)
                    {
                        if (member != null && member.Type.Kind == TypeKind.Delegate && !csharpInvocation.IsExtensionMethodInvocation)
                        {
                            throw new EmitterException(invocationExpression, "Delegate's methods are not supported. Please use direct delegate invoke.");
                        }

                        if(csharpInvocation.TargetResult.IsCompileTimeConstant) {
                            if(csharpInvocation.Member.Name == "ToString") {
                                if(invocationExpression.IsSingleExpression()) {
                                    this.Write("--");
                                }
                                if(csharpInvocation.TargetResult.Type.Kind == TypeKind.Enum) {
                                    MemberResolveResult reslut = (MemberResolveResult)csharpInvocation.TargetResult;
                                    this.Write("\"", reslut.Member.Name, "\"");
                                }
                                else {
                                    this.Write(csharpInvocation.TargetResult.ConstantValue);
                                }
                                this.Emitter.ReplaceAwaiterByVar = oldValue;
                                this.Emitter.AsyncExpressionHandling = oldAsyncExpressionHandling;
                                return;
                            }                         
                        }
                        else {
                            if(csharpInvocation.TargetResult.Type.Kind == TypeKind.Enum) {
                                string attrName = Bridge.Translator.Translator.Bridge_ASSEMBLY + ".EnumExportAttribute";
                                var attr = csharpInvocation.Member.Attributes.FirstOrDefault(i => i.AttributeType.FullName == attrName);
                                if(attr != null) {
                                    TransformCtx.ExportEnums.Add(csharpInvocation.TargetResult.Type);
                                }
                            }
                            else if(csharpInvocation.TargetResult.Type.FullName == "System.Enum") {
                                string attrName = Bridge.Translator.Translator.Bridge_ASSEMBLY + ".EnumExportAttribute";
                                var attr = csharpInvocation.Member.Attributes.FirstOrDefault(i => i.AttributeType.FullName == attrName);
                                if(attr != null) {
                                    string typeName = attr.PositionalArguments[0].ConstantValue.ToString();
                                    var typeArgument = argsInfo.TypeArguments.FirstOrDefault(i => i.Name == typeName);
                                    if(typeArgument != null) {
                                        if(typeArgument.IType != null) {
                                            TransformCtx.ExportEnums.Add(typeArgument.IType);
                                        }
                                        else {
                                            ResolveResult resolveResult = this.Emitter.Resolver.ResolveNode(typeArgument.AstType, this.Emitter);
                                            if(resolveResult != null) {
                                                TransformCtx.ExportEnums.Add(resolveResult.Type);
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (csharpInvocation.IsExtensionMethodInvocation)
                        {
                            invocationResult = csharpInvocation;
                            isExtensionMethodInvocation = true;
                            var resolvedMethod = invocationResult.Member as IMethod;
                            if (resolvedMethod != null && resolvedMethod.IsExtensionMethod)
                            {
                                string inline = this.Emitter.GetInline(resolvedMethod);
                                bool isNative = this.IsNativeMethod(resolvedMethod);

                                if (string.IsNullOrWhiteSpace(inline) && isNative)
                                {
                                    invocationResult = null;
                                }
                            }
                        }
                        else
                        {
                            invocationResult = null;
                        }

                        if (this.IsEmptyPartialInvoking(csharpInvocation.Member as IMethod))
                        {
                            this.Emitter.SkipSemiColon = true;
                            this.Emitter.ReplaceAwaiterByVar = oldValue;
                            this.Emitter.AsyncExpressionHandling = oldAsyncExpressionHandling;

                            return;
                        }
                    }
                    else
                    {
                        invocationResult = targetResolve as InvocationResolveResult;

                        if (invocationResult != null && this.IsEmptyPartialInvoking(invocationResult.Member as IMethod))
                        {
                            this.Emitter.SkipSemiColon = true;
                            this.Emitter.ReplaceAwaiterByVar = oldValue;
                            this.Emitter.AsyncExpressionHandling = oldAsyncExpressionHandling;

                            return;
                        }
                    }

                    if (invocationResult == null)
                    {
                        invocationResult = this.Emitter.Resolver.ResolveNode(invocationExpression, this.Emitter) as InvocationResolveResult;
                    }

                    if (invocationResult != null)
                    {
                        var resolvedMethod = invocationResult.Member as IMethod;

                        if (resolvedMethod != null && resolvedMethod.IsExtensionMethod)
                        {
                            string inline = this.Emitter.GetInline(resolvedMethod);
                            bool isNative = this.IsNativeMethod(resolvedMethod);

                            if (isExtensionMethodInvocation)
                            {
                                if (!string.IsNullOrWhiteSpace(inline))
                                {
                                    this.Write("");
                                    StringBuilder savedBuilder = this.Emitter.Output;
                                    this.Emitter.Output = new StringBuilder();
                                    this.WriteThisExtension(invocationExpression.Target);
                                    argsInfo.ThisArgument = this.Emitter.Output.ToString();
                                    this.Emitter.Output = savedBuilder;
                                    new InlineArgumentsBlock(this.Emitter, argsInfo, inline).Emit();
                                }
                                else if (!isNative)
                                {
                                    var overloads = OverloadsCollection.Create(this.Emitter, resolvedMethod);
                                    string name = BridgeTypes.ToJsName(resolvedMethod.DeclaringType, this.Emitter) + "." + overloads.GetOverloadName();
                                    var isIgnoreClass = resolvedMethod.DeclaringTypeDefinition != null && this.Emitter.Validator.IsIgnoreType(resolvedMethod.DeclaringTypeDefinition);

                                    this.Write(name);
                                    this.WriteOpenParentheses();
                                    this.WriteThisExtension(invocationExpression.Target);
                                    if(invocationExpression.Arguments.Count > 0) {
                                        this.WriteComma();
                                    }
                                    new ExpressionListBlock(this.Emitter, argsExpressions, paramsArg, invocationExpression).Emit();
                                    if(!isIgnoreClass && argsInfo.HasTypeArguments) {
                                        this.WriteComma();
                                        foreach(var nullParams in argsExpressions.SkipWhile(i => i != null)) {
                                            this.Write("nil");
                                            this.WriteComma();
                                        }
                                        new TypeExpressionListBlock(this.Emitter, argsInfo.TypeArguments).Emit();
                                    }
                                    this.WriteCloseParentheses();
                                }

                                if (!string.IsNullOrWhiteSpace(inline) || !isNative)
                                {
                                    this.Emitter.ReplaceAwaiterByVar = oldValue;
                                    this.Emitter.AsyncExpressionHandling = oldAsyncExpressionHandling;

                                    return;
                                }
                            }
                            else if (isNative)
                            {
                                if (!string.IsNullOrWhiteSpace(inline))
                                {
                                    this.Write("");
                                    StringBuilder savedBuilder = this.Emitter.Output;
                                    this.Emitter.Output = new StringBuilder();
                                    this.WriteThisExtension(invocationExpression.Target);
                                    argsInfo.ThisArgument = this.Emitter.Output.ToString();
                                    this.Emitter.Output = savedBuilder;
                                    new InlineArgumentsBlock(this.Emitter, argsInfo, inline).Emit();
                                }
                                else
                                {
                                    argsExpressions.First().AcceptVisitor(this.Emitter);
                                    this.WriteDot();
                                    string name = this.Emitter.GetEntityName(resolvedMethod);
                                    this.Write(name);
                                    this.WriteOpenParentheses();
                                    new ExpressionListBlock(this.Emitter, argsExpressions.Skip(1), paramsArg, invocationExpression).Emit();
                                    this.WriteCloseParentheses();
                                }

                                this.Emitter.ReplaceAwaiterByVar = oldValue;
                                this.Emitter.AsyncExpressionHandling = oldAsyncExpressionHandling;
                                return;
                            }
                        }
                    }
                }
            }

            var proto = false;
            if (targetMember != null && targetMember.Target is BaseReferenceExpression)
            {
                var rr = this.Emitter.Resolver.ResolveNode(targetMember, this.Emitter) as MemberResolveResult;

                if (rr != null)
                {
                    proto = rr.IsVirtualCall;

                    /*var method = rr.Member as IMethod;
                    if (method != null && method.IsVirtual)
                    {
                        proto = true;
                    }
                    else
                    {
                        var prop = rr.Member as IProperty;

                        if (prop != null && prop.IsVirtual)
                        {
                            proto = true;
                        }
                    }*/
                }
            }

            if (proto)
            {
                var baseType = this.Emitter.GetBaseMethodOwnerTypeDefinition(targetMember.MemberName, targetMember.TypeArguments.Count);
                var method = invocationExpression.GetParent<MethodDeclaration>();

                bool isIgnore = this.Emitter.Validator.IsIgnoreType(baseType);

                if (isIgnore)
                {
                    //throw (System.Exception)this.Emitter.CreateException(targetMember.Target, "Cannot call base method, because parent class code is ignored");
                }

                bool needComma = false;

                var resolveResult = this.Emitter.Resolver.ResolveNode(targetMember, this.Emitter);

                string name = null;

                if (this.Emitter.TypeInfo.GetBaseTypes(this.Emitter).Any())
                {
                    name = BridgeTypes.ToJsName(this.Emitter.TypeInfo.GetBaseClass(this.Emitter), this.Emitter);
                }
                else
                {
                    name = BridgeTypes.ToJsName(baseType, this.Emitter);
                }

                string baseMethod;

                if (resolveResult is InvocationResolveResult)
                {
                    InvocationResolveResult invocationResult = (InvocationResolveResult)resolveResult;
                    baseMethod = OverloadsCollection.Create(this.Emitter, invocationResult.Member).GetOverloadName();
                }
                else if (resolveResult is MemberResolveResult)
                {
                    MemberResolveResult memberResult = (MemberResolveResult)resolveResult;
                    baseMethod = OverloadsCollection.Create(this.Emitter, memberResult.Member).GetOverloadName();
                }
                else
                {
                    baseMethod = targetMember.MemberName;
                    baseMethod = this.Emitter.AssemblyInfo.PreserveMemberCase ? baseMethod : Object.Net.Utilities.StringUtils.ToLowerCamelCase(baseMethod);
                }

                this.Write(name, '.', baseMethod);

                if (!isIgnore && argsInfo.HasTypeArguments)
                {
                    this.WriteOpenParentheses();
                    new TypeExpressionListBlock(this.Emitter, argsInfo.TypeArguments).Emit();
                    this.WriteCloseParentheses();
                }
                this.WriteOpenParentheses();

                this.WriteThis();
                needComma = true;

                foreach (var arg in argsExpressions)
                {
                    if (needComma)
                    {
                        this.WriteComma();
                    }

                    needComma = true;
                    arg.AcceptVisitor(this.Emitter);
                }

                this.WriteCloseParentheses();
            }
            else
            {
                var dynamicResolveResult = this.Emitter.Resolver.ResolveNode(invocationExpression, this.Emitter) as DynamicInvocationResolveResult;

                if (dynamicResolveResult != null)
                {
                    var group = dynamicResolveResult.Target as MethodGroupResolveResult;

                    if (group != null && group.Methods.Count() > 1)
                    {
                        throw new EmitterException(invocationExpression, "Cannot compile this dynamic invocation because there are two or more method overloads with the same parameter count. To work around this limitation, assign the dynamic value to a non-dynamic variable before use or call a method with different parameter count");
                    }
                }

                var targetResolveResult = this.Emitter.Resolver.ResolveNode(invocationExpression.Target, this.Emitter);
                var invocationResolveResult = targetResolveResult as MemberResolveResult;
                IMethod method = null;

                if (invocationResolveResult != null)
                {
                    method = invocationResolveResult.Member as IMethod;
                }

                if (this.IsEmptyPartialInvoking(method))
                {
                    this.Emitter.SkipSemiColon = true;
                    this.Emitter.ReplaceAwaiterByVar = oldValue;
                    this.Emitter.AsyncExpressionHandling = oldAsyncExpressionHandling;
                    return;
                }

                int count = this.Emitter.Writers.Count;
                invocationExpression.Target.AcceptVisitor(this.Emitter);

                if (this.Emitter.Writers.Count > count)
                {
                    var tuple = this.Emitter.Writers.Pop();

                    if (method != null && method.IsExtensionMethod)
                    {
                        StringBuilder savedBuilder = this.Emitter.Output;
                        this.Emitter.Output = new StringBuilder();
                        this.WriteThisExtension(invocationExpression.Target);
                        argsInfo.ThisArgument = this.Emitter.Output.ToString();
                        this.Emitter.Output = savedBuilder;
                    }

                    new InlineArgumentsBlock(this.Emitter, argsInfo, tuple.Item1).Emit();
                    var result = this.Emitter.Output.ToString();
                    this.Emitter.Output = tuple.Item2;
                    this.Emitter.IsNewLine = tuple.Item3;
                    this.Write(result);
                }
                else
                {
                    var isIgnore = false;

                    if (method != null && method.DeclaringTypeDefinition != null && this.Emitter.Validator.IsIgnoreType(method.DeclaringTypeDefinition))
                    {
                        isIgnore = true;
                    }

                    this.WriteOpenParentheses();
                    if(method != null && !method.IsStatic && method.DeclaringType == TransformCtx.CurClass) {
                        this.WriteThis();
                        if(argsExpressions.Length > 0) {
                            this.WriteComma();
                        }
                    }
                    
                    new ExpressionListBlock(this.Emitter, argsExpressions, paramsArg, invocationExpression).Emit();
                    if(!isIgnore && argsInfo.HasTypeArguments) {
                        if(argsExpressions.Length > 0 || (!method.IsStatic && method.DeclaringType == TransformCtx.CurClass)) {
                            this.WriteComma();
                        }
                        new TypeExpressionListBlock(this.Emitter, argsInfo.TypeArguments).Emit();
                    }
                    this.WriteCloseParentheses();
                }
            }

            Helpers.CheckValueTypeClone(this.Emitter.Resolver.ResolveNode(invocationExpression, this.Emitter), invocationExpression, this, pos);

            this.Emitter.ReplaceAwaiterByVar = oldValue;
            this.Emitter.AsyncExpressionHandling = oldAsyncExpressionHandling;
        }

        private bool IsNativeMethod(IMethod resolvedMethod)
        {
            return resolvedMethod.DeclaringTypeDefinition != null &&
                   this.Emitter.Validator.IsIgnoreType(resolvedMethod.DeclaringTypeDefinition);
        }
    }
}
