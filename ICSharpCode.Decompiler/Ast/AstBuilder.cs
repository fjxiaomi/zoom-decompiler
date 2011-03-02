using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Decompiler.Transforms;
using ICSharpCode.Decompiler;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.Utils;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Ast = ICSharpCode.NRefactory.CSharp;
using ClassType = ICSharpCode.NRefactory.TypeSystem.ClassType;
using VarianceModifier = ICSharpCode.NRefactory.TypeSystem.VarianceModifier;

namespace Decompiler
{
	public class AstBuilder
	{
		DecompilerContext context = new DecompilerContext();
		CompilationUnit astCompileUnit = new CompilationUnit();
		Dictionary<string, NamespaceDeclaration> astNamespaces = new Dictionary<string, NamespaceDeclaration>();
		
		public AstBuilder(DecompilerContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");
			this.context = context;
		}
		
		public static bool MemberIsHidden(MemberReference member)
		{
			MethodDefinition method = member as MethodDefinition;
			if (method != null && (method.IsGetter || method.IsSetter || method.IsAddOn || method.IsRemoveOn))
				return true;
			if (method != null && method.Name.StartsWith("<", StringComparison.Ordinal) && method.IsCompilerGenerated())
				return true;
			TypeDefinition type = member as TypeDefinition;
			if (type != null && type.DeclaringType != null && type.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal) && type.IsCompilerGenerated())
				return true;
			FieldDefinition field = member as FieldDefinition;
			if (field != null && field.Name.StartsWith("CS$<>", StringComparison.Ordinal) && field.IsCompilerGenerated())
				return true;
			return false;
		}
		
		public void GenerateCode(ITextOutput output)
		{
			GenerateCode(output, null);
		}
		
		public void GenerateCode(ITextOutput output, Predicate<IAstTransform> transformAbortCondition)
		{
			TransformationPipeline.RunTransformationsUntil(astCompileUnit, transformAbortCondition, context);
			astCompileUnit.AcceptVisitor(new InsertParenthesesVisitor { InsertParenthesesForReadability = true }, null);
			
			var outputFormatter = new TextOutputFormatter(output);
			var formattingPolicy = new CSharpFormattingPolicy();
			// disable whitespace in front of parentheses:
			formattingPolicy.BeforeMethodCallParentheses = false;
			formattingPolicy.BeforeMethodDeclarationParentheses = false;
			formattingPolicy.BeforeConstructorDeclarationParentheses = false;
			formattingPolicy.BeforeDelegateDeclarationParentheses = false;
			astCompileUnit.AcceptVisitor(new OutputVisitor(outputFormatter, formattingPolicy), null);
		}
		
		public void AddAssembly(AssemblyDefinition assemblyDefinition, bool onlyAssemblyLevel = false)
		{
			astCompileUnit.AddChild(
				new UsingDeclaration {
					Import = new SimpleType("System")
				}, CompilationUnit.MemberRole);
			
			ConvertCustomAttributes(astCompileUnit, assemblyDefinition, AttributeTarget.Assembly);
			ConvertCustomAttributes(astCompileUnit, assemblyDefinition.MainModule, AttributeTarget.Module);

			if (!onlyAssemblyLevel) {
				foreach (TypeDefinition typeDef in assemblyDefinition.MainModule.Types)
				{
					// Skip nested types - they will be added by the parent type
					if (typeDef.DeclaringType != null) continue;
					// Skip the <Module> class
					if (typeDef.Name == "<Module>") continue;

					AddType(typeDef);
				}
			}
		}
		
		NamespaceDeclaration GetCodeNamespace(string name)
		{
			if (string.IsNullOrEmpty(name)) {
				return null;
			}
			if (astNamespaces.ContainsKey(name)) {
				return astNamespaces[name];
			} else {
				// Create the namespace
				NamespaceDeclaration astNamespace = new NamespaceDeclaration { Name = name };
				astCompileUnit.AddChild(astNamespace, CompilationUnit.MemberRole);
				astNamespaces[name] = astNamespace;
				return astNamespace;
			}
		}
		
		public void AddType(TypeDefinition typeDef)
		{
			TypeDeclaration astType = CreateType(typeDef);
			NamespaceDeclaration astNS = GetCodeNamespace(typeDef.Namespace);
			if (astNS != null) {
				astNS.AddChild(astType, NamespaceDeclaration.MemberRole);
			} else {
				astCompileUnit.AddChild(astType, CompilationUnit.MemberRole);
			}
		}
		
		public void AddMethod(MethodDefinition method)
		{
			AstNode node = method.IsConstructor ? (AstNode)CreateConstructor(method) : CreateMethod(method);
			astCompileUnit.AddChild(node, CompilationUnit.MemberRole);
		}
		
		public void AddProperty(PropertyDefinition property)
		{
			astCompileUnit.AddChild(CreateProperty(property), CompilationUnit.MemberRole);
		}
		
		public void AddField(FieldDefinition field)
		{
			astCompileUnit.AddChild(CreateField(field), CompilationUnit.MemberRole);
		}
		
		public void AddEvent(EventDefinition ev)
		{
			astCompileUnit.AddChild(CreateEvent(ev), CompilationUnit.MemberRole);
		}
		
		public TypeDeclaration CreateType(TypeDefinition typeDef)
		{
			TypeDeclaration astType = new TypeDeclaration();
			astType.AddAnnotation(typeDef);
			astType.Modifiers = ConvertModifiers(typeDef);
			astType.Name = CleanName(typeDef.Name);
			
			if (typeDef.IsEnum) {  // NB: Enum is value type
				astType.ClassType = ClassType.Enum;
				astType.Modifiers &= ~Modifiers.Sealed;
			} else if (typeDef.IsValueType) {
				astType.ClassType = ClassType.Struct;
				astType.Modifiers &= ~Modifiers.Sealed;
			} else if (typeDef.IsInterface) {
				astType.ClassType = ClassType.Interface;
				astType.Modifiers &= ~Modifiers.Abstract;
			} else {
				astType.ClassType = ClassType.Class;
			}
			
			astType.TypeParameters.AddRange(MakeTypeParameters(typeDef.GenericParameters));
			astType.Constraints.AddRange(MakeConstraints(typeDef.GenericParameters));
			
			// Nested types
			foreach(TypeDefinition nestedTypeDef in typeDef.NestedTypes) {
				if (MemberIsHidden(nestedTypeDef))
					continue;
				astType.AddChild(CreateType(nestedTypeDef), TypeDeclaration.MemberRole);
			}
			
			
			if (typeDef.IsEnum) {
				long expectedEnumMemberValue = 0;
				bool forcePrintingInitializers = IsFlagsEnum(typeDef);
				foreach (FieldDefinition field in typeDef.Fields) {
					if (field.IsRuntimeSpecialName) {
						// the value__ field
						astType.AddChild(ConvertType(field.FieldType), TypeDeclaration.BaseTypeRole);
					} else {
						EnumMemberDeclaration enumMember = new EnumMemberDeclaration();
						enumMember.Name = CleanName(field.Name);
						long memberValue = (long)CSharpPrimitiveCast.Cast(TypeCode.Int64, field.Constant, false);
						if (forcePrintingInitializers || memberValue != expectedEnumMemberValue) {
							enumMember.AddChild(new PrimitiveExpression(field.Constant), EnumMemberDeclaration.InitializerRole);
						}
						expectedEnumMemberValue = memberValue + 1;
						astType.AddChild(enumMember, TypeDeclaration.MemberRole);
					}
				}
			} else {
				// Base type
				if (typeDef.BaseType != null && !typeDef.IsValueType && typeDef.BaseType.FullName != "System.Object") {
					astType.AddChild(ConvertType(typeDef.BaseType), TypeDeclaration.BaseTypeRole);
				}
				foreach (var i in typeDef.Interfaces)
					astType.AddChild(ConvertType(i), TypeDeclaration.BaseTypeRole);
				
				
				AddTypeMembers(astType, typeDef);
			}

			ConvertCustomAttributes(astType, typeDef);
			return astType;
		}

		public void Transform(IAstTransform transform)
		{
			transform.Run(astCompileUnit);
		}
		
		string CleanName(string name)
		{
			int pos = name.LastIndexOf('`');
			if (pos >= 0)
				name = name.Substring(0, pos);
			return name;
		}
		
		#region Convert Type Reference
		/// <summary>
		/// Converts a type reference.
		/// </summary>
		/// <param name="type">The Cecil type reference that should be converted into
		/// a type system type reference.</param>
		/// <param name="typeAttributes">Attributes associated with the Cecil type reference.
		/// This is used to support the 'dynamic' type.</param>
		public static AstType ConvertType(TypeReference type, ICustomAttributeProvider typeAttributes = null)
		{
			int typeIndex = 0;
			return ConvertType(type, typeAttributes, ref typeIndex);
		}
		
		static AstType ConvertType(TypeReference type, ICustomAttributeProvider typeAttributes, ref int typeIndex)
		{
			while (type is OptionalModifierType || type is RequiredModifierType) {
				type = ((TypeSpecification)type).ElementType;
			}
			if (type == null) {
				return AstType.Null;
			}
			
			if (type is Mono.Cecil.ByReferenceType) {
				typeIndex++;
				// ignore by reference type (cannot be represented in C#)
				return ConvertType((type as Mono.Cecil.ByReferenceType).ElementType, typeAttributes, ref typeIndex);
			} else if (type is Mono.Cecil.PointerType) {
				typeIndex++;
				return ConvertType((type as Mono.Cecil.PointerType).ElementType, typeAttributes, ref typeIndex)
					.MakePointerType();
			} else if (type is Mono.Cecil.ArrayType) {
				typeIndex++;
				return ConvertType((type as Mono.Cecil.ArrayType).ElementType, typeAttributes, ref typeIndex)
					.MakeArrayType((type as Mono.Cecil.ArrayType).Rank);
			} else if (type is GenericInstanceType) {
				GenericInstanceType gType = (GenericInstanceType)type;
				if (gType.ElementType.Namespace == "System" && gType.ElementType.Name == "Nullable`1" && gType.GenericArguments.Count == 1) {
					typeIndex++;
					return new ComposedType {
						BaseType = ConvertType(gType.GenericArguments[0], typeAttributes, ref typeIndex),
						HasNullableSpecifier = true
					};
				}
				AstType baseType = ConvertType(gType.ElementType, typeAttributes, ref typeIndex);
				foreach (var typeArgument in gType.GenericArguments) {
					typeIndex++;
					baseType.AddChild(ConvertType(typeArgument, typeAttributes, ref typeIndex), AstType.Roles.TypeArgument);
				}
				return baseType;
			} else if (type is GenericParameter) {
				return new SimpleType(type.Name);
			} else if (type.IsNested) {
				AstType typeRef = ConvertType(type.DeclaringType, typeAttributes, ref typeIndex);
				string namepart = ICSharpCode.NRefactory.TypeSystem.ReflectionHelper.SplitTypeParameterCountFromReflectionName(type.Name);
				return new MemberType { Target = typeRef, MemberName = namepart }.WithAnnotation(type);
			} else {
				string ns = type.Namespace ?? string.Empty;
				string name = type.Name;
				if (name == null)
					throw new InvalidOperationException("type.Name returned null. Type: " + type.ToString());
				
				if (name == "Object" && ns == "System" && HasDynamicAttribute(typeAttributes, typeIndex)) {
					return new PrimitiveType("dynamic");
				} else {
					if (ns == "System") {
						switch (name) {
							case "SByte":
								return new PrimitiveType("sbyte");
							case "Int16":
								return new PrimitiveType("short");
							case "Int32":
								return new PrimitiveType("int");
							case "Int64":
								return new PrimitiveType("long");
							case "Byte":
								return new PrimitiveType("byte");
							case "UInt16":
								return new PrimitiveType("ushort");
							case "UInt32":
								return new PrimitiveType("uint");
							case "UInt64":
								return new PrimitiveType("ulong");
							case "String":
								return new PrimitiveType("string");
							case "Single":
								return new PrimitiveType("float");
							case "Double":
								return new PrimitiveType("double");
							case "Decimal":
								return new PrimitiveType("decimal");
							case "Char":
								return new PrimitiveType("char");
							case "Boolean":
								return new PrimitiveType("bool");
							case "Void":
								return new PrimitiveType("void");
							case "Object":
								return new PrimitiveType("object");
						}
					}
					
					name = ICSharpCode.NRefactory.TypeSystem.ReflectionHelper.SplitTypeParameterCountFromReflectionName(name);
					
					// TODO: Until we can simplify type with 'using', use just the name without namesapce
					return new SimpleType(name).WithAnnotation(type);
					
//					if (ns.Length == 0)
//						return new SimpleType(name).WithAnnotation(type);
//					string[] parts = ns.Split('.');
//					AstType nsType = new SimpleType(parts[0]);
//					for (int i = 1; i < parts.Length; i++) {
//						nsType = new MemberType { Target = nsType, MemberName = parts[i] };
//					}
//					return new MemberType { Target = nsType, MemberName = name }.WithAnnotation(type);
				}
			}
		}
		
		const string DynamicAttributeFullName = "System.Runtime.CompilerServices.DynamicAttribute";
		
		static bool HasDynamicAttribute(ICustomAttributeProvider attributeProvider, int typeIndex)
		{
			if (attributeProvider == null || !attributeProvider.HasCustomAttributes)
				return false;
			foreach (CustomAttribute a in attributeProvider.CustomAttributes) {
				if (a.Constructor.DeclaringType.FullName == DynamicAttributeFullName) {
					if (a.ConstructorArguments.Count == 1) {
						CustomAttributeArgument[] values = a.ConstructorArguments[0].Value as CustomAttributeArgument[];
						if (values != null && typeIndex < values.Length && values[typeIndex].Value is bool)
							return (bool)values[typeIndex].Value;
					}
					return true;
				}
			}
			return false;
		}
		#endregion
		
		#region ConvertModifiers
		Modifiers ConvertModifiers(TypeDefinition typeDef)
		{
			Modifiers modifiers = Modifiers.None;
			if (typeDef.IsNestedPrivate)
				modifiers |= Modifiers.Private;
			else if (typeDef.IsNestedAssembly || typeDef.IsNestedFamilyAndAssembly || typeDef.IsNotPublic)
				modifiers |= Modifiers.Internal;
			else if (typeDef.IsNestedFamily)
				modifiers |= Modifiers.Protected;
			else if (typeDef.IsNestedFamilyOrAssembly)
				modifiers |= Modifiers.Protected | Modifiers.Internal;
			else if (typeDef.IsPublic || typeDef.IsNestedPublic)
				modifiers |= Modifiers.Public;
			
			if (typeDef.IsAbstract && typeDef.IsSealed)
				modifiers |= Modifiers.Static;
			else if (typeDef.IsAbstract)
				modifiers |= Modifiers.Abstract;
			else if (typeDef.IsSealed)
				modifiers |= Modifiers.Sealed;
			
			return modifiers;
		}
		
		Modifiers ConvertModifiers(FieldDefinition fieldDef)
		{
			Modifiers modifiers = Modifiers.None;
			if (fieldDef.IsPrivate)
				modifiers |= Modifiers.Private;
			else if (fieldDef.IsAssembly || fieldDef.IsFamilyAndAssembly)
				modifiers |= Modifiers.Internal;
			else if (fieldDef.IsFamily)
				modifiers |= Modifiers.Protected;
			else if (fieldDef.IsFamilyOrAssembly)
				modifiers |= Modifiers.Protected | Modifiers.Internal;
			else if (fieldDef.IsPublic)
				modifiers |= Modifiers.Public;
			
			if (fieldDef.IsLiteral) {
				modifiers |= Modifiers.Const;
			} else {
				if (fieldDef.IsStatic)
					modifiers |= Modifiers.Static;
				
				if (fieldDef.IsInitOnly)
					modifiers |= Modifiers.Readonly;
			}
			
			return modifiers;
		}
		
		Modifiers ConvertModifiers(MethodDefinition methodDef)
		{
			if (methodDef == null)
				return Modifiers.None;
			Modifiers modifiers = Modifiers.None;
			if (methodDef.IsPrivate)
				modifiers |= Modifiers.Private;
			else if (methodDef.IsAssembly || methodDef.IsFamilyAndAssembly)
				modifiers |= Modifiers.Internal;
			else if (methodDef.IsFamily)
				modifiers |= Modifiers.Protected;
			else if (methodDef.IsFamilyOrAssembly)
				modifiers |= Modifiers.Protected | Modifiers.Internal;
			else if (methodDef.IsPublic)
				modifiers |= Modifiers.Public;
			
			if (methodDef.IsStatic)
				modifiers |= Modifiers.Static;
			
			if (methodDef.IsAbstract) {
				modifiers |= Modifiers.Abstract;
				if (!methodDef.IsNewSlot)
					modifiers |= Modifiers.Override;
			} else if (methodDef.IsFinal) {
				if (!methodDef.IsNewSlot) {
					modifiers |= Modifiers.Sealed | Modifiers.Override;
				}
			} else if (methodDef.IsVirtual) {
				if (methodDef.IsNewSlot)
					modifiers |= Modifiers.Virtual;
				else
					modifiers |= Modifiers.Override;
			}
			if (!methodDef.HasBody && !methodDef.IsAbstract)
				modifiers |= Modifiers.Extern;
			
			return modifiers;
		}
		#endregion
		
		void AddTypeMembers(TypeDeclaration astType, TypeDefinition typeDef)
		{
			// Add fields
			foreach(FieldDefinition fieldDef in typeDef.Fields) {
				if (MemberIsHidden(fieldDef)) continue;
				astType.AddChild(CreateField(fieldDef), TypeDeclaration.MemberRole);
			}
			
			// Add events
			foreach(EventDefinition eventDef in typeDef.Events) {
				astType.AddChild(CreateEvent(eventDef), TypeDeclaration.MemberRole);
			}
			
			// Add properties
			foreach(PropertyDefinition propDef in typeDef.Properties) {
				astType.AddChild(CreateProperty(propDef), TypeDeclaration.MemberRole);
			}
			
			// Add constructors
			foreach(MethodDefinition methodDef in typeDef.Methods) {
				if (!methodDef.IsConstructor) continue;
				
				astType.AddChild(CreateConstructor(methodDef), TypeDeclaration.MemberRole);
			}
			
			// Add methods
			foreach(MethodDefinition methodDef in typeDef.Methods) {
				if (methodDef.IsConstructor || MemberIsHidden(methodDef)) continue;
				
				astType.AddChild(CreateMethod(methodDef), TypeDeclaration.MemberRole);
			}
		}

		MethodDeclaration CreateMethod(MethodDefinition methodDef)
		{
			MethodDeclaration astMethod = new MethodDeclaration();
			astMethod.AddAnnotation(methodDef);
			astMethod.ReturnType = ConvertType(methodDef.ReturnType, methodDef.MethodReturnType);
			astMethod.Name = CleanName(methodDef.Name);
			astMethod.TypeParameters.AddRange(MakeTypeParameters(methodDef.GenericParameters));
			astMethod.Parameters.AddRange(MakeParameters(methodDef.Parameters));
			astMethod.Constraints.AddRange(MakeConstraints(methodDef.GenericParameters));
			if (!methodDef.DeclaringType.IsInterface) {
				astMethod.Modifiers = ConvertModifiers(methodDef);
				astMethod.Body = AstMethodBodyBuilder.CreateMethodBody(methodDef, context);
			}
			ConvertCustomAttributes(astMethod, methodDef);
			ConvertCustomAttributes(astMethod, methodDef.MethodReturnType, AttributeTarget.Return);
			return astMethod;
		}
		
		IEnumerable<TypeParameterDeclaration> MakeTypeParameters(IEnumerable<GenericParameter> genericParameters)
		{
			return genericParameters.Select(
				gp => new TypeParameterDeclaration {
					Name = CleanName(gp.Name),
					Variance = gp.IsContravariant ? VarianceModifier.Contravariant : gp.IsCovariant ? VarianceModifier.Covariant : VarianceModifier.Invariant
				});
		}
		
		IEnumerable<Constraint> MakeConstraints(IEnumerable<GenericParameter> genericParameters)
		{
			// TODO
			return Enumerable.Empty<Constraint>();
		}
		
		ConstructorDeclaration CreateConstructor(MethodDefinition methodDef)
		{
			ConstructorDeclaration astMethod = new ConstructorDeclaration();
			astMethod.AddAnnotation(methodDef);
			astMethod.Modifiers = ConvertModifiers(methodDef);
			if (methodDef.IsStatic) {
				// don't show visibility for static ctors
				astMethod.Modifiers &= ~Modifiers.VisibilityMask;
			}
			astMethod.Name = CleanName(methodDef.DeclaringType.Name);
			astMethod.Parameters.AddRange(MakeParameters(methodDef.Parameters));
			astMethod.Body = AstMethodBodyBuilder.CreateMethodBody(methodDef, context);
			return astMethod;
		}

		PropertyDeclaration CreateProperty(PropertyDefinition propDef)
		{
			PropertyDeclaration astProp = new PropertyDeclaration();
			astProp.AddAnnotation(propDef);
			astProp.Modifiers = ConvertModifiers(propDef.GetMethod ?? propDef.SetMethod);
			astProp.Name = CleanName(propDef.Name);
			astProp.ReturnType = ConvertType(propDef.PropertyType, propDef);
			if (propDef.GetMethod != null) {
				astProp.Getter = new Accessor {
					Body = AstMethodBodyBuilder.CreateMethodBody(propDef.GetMethod, context)
				}.WithAnnotation(propDef.GetMethod);
				ConvertCustomAttributes(astProp.Getter, propDef.GetMethod);
				ConvertCustomAttributes(astProp.Getter, propDef.GetMethod.MethodReturnType, AttributeTarget.Return);
			}
			if (propDef.SetMethod != null) {
				astProp.Setter = new Accessor {
					Body = AstMethodBodyBuilder.CreateMethodBody(propDef.SetMethod, context)
				}.WithAnnotation(propDef.SetMethod);
				ConvertCustomAttributes(astProp.Setter, propDef.SetMethod);
				ConvertCustomAttributes(astProp.Setter, propDef.SetMethod.MethodReturnType, AttributeTarget.Return);
				ConvertCustomAttributes(astProp.Setter, propDef.SetMethod.Parameters.Last(), AttributeTarget.Param);
			}
			ConvertCustomAttributes(astProp, propDef);
			return astProp;
		}

		CustomEventDeclaration CreateEvent(EventDefinition eventDef)
		{
			CustomEventDeclaration astEvent = new CustomEventDeclaration();
			astEvent.AddAnnotation(eventDef);
			astEvent.Name = CleanName(eventDef.Name);
			astEvent.ReturnType = ConvertType(eventDef.EventType, eventDef);
			astEvent.Modifiers = ConvertModifiers(eventDef.AddMethod);
			if (eventDef.AddMethod != null) {
				astEvent.AddAccessor = new Accessor {
					Body = AstMethodBodyBuilder.CreateMethodBody(eventDef.AddMethod, context)
				}.WithAnnotation(eventDef.AddMethod);
			}
			if (eventDef.RemoveMethod != null) {
				astEvent.RemoveAccessor = new Accessor {
					Body = AstMethodBodyBuilder.CreateMethodBody(eventDef.RemoveMethod, context)
				}.WithAnnotation(eventDef.RemoveMethod);
			}
			return astEvent;
		}

		FieldDeclaration CreateField(FieldDefinition fieldDef)
		{
			FieldDeclaration astField = new FieldDeclaration();
			astField.AddAnnotation(fieldDef);
			VariableInitializer initializer = new VariableInitializer(CleanName(fieldDef.Name));
			astField.AddChild(initializer, FieldDeclaration.Roles.Variable);
			astField.ReturnType = ConvertType(fieldDef.FieldType, fieldDef);
			astField.Modifiers = ConvertModifiers(fieldDef);
			if (fieldDef.HasConstant) {
				if (fieldDef.Constant == null)
					initializer.Initializer = new NullReferenceExpression();
				else
					initializer.Initializer = new PrimitiveExpression(fieldDef.Constant);
			}
			ConvertCustomAttributes(astField, fieldDef);
			return astField;
		}
		
		public static IEnumerable<ParameterDeclaration> MakeParameters(IEnumerable<ParameterDefinition> paramCol)
		{
			foreach(ParameterDefinition paramDef in paramCol) {
				ParameterDeclaration astParam = new ParameterDeclaration();
				astParam.Type = ConvertType(paramDef.ParameterType, paramDef);
				astParam.Name = paramDef.Name;
				
				if (paramDef.ParameterType is ByReferenceType) {
					astParam.ParameterModifier = paramDef.IsOut ? ParameterModifier.Out : ParameterModifier.Ref;
				}
				// TODO: params, this
				
				ConvertCustomAttributes(astParam, paramDef);
				yield return astParam;
			}
		}

		static void ConvertCustomAttributes(AstNode attributedNode, ICustomAttributeProvider customAttributeProvider, AttributeTarget target = AttributeTarget.None)
		{
			if (customAttributeProvider.HasCustomAttributes) {
				var attributes = new List<ICSharpCode.NRefactory.CSharp.Attribute>();
				foreach (var customAttribute in customAttributeProvider.CustomAttributes) {
					var attribute = new ICSharpCode.NRefactory.CSharp.Attribute();
					attribute.Type = ConvertType(customAttribute.AttributeType);
					attributes.Add(attribute);

					if(customAttribute.HasConstructorArguments) {
						foreach (var parameter in customAttribute.ConstructorArguments) {
							Expression parameterValue = ConvertArgumentValue(parameter);
							attribute.Arguments.Add(parameterValue);
						}
					}
					if (customAttribute.HasProperties) {
						foreach (var propertyNamedArg in customAttribute.Properties) {
							var propertyReference = customAttribute.AttributeType.Resolve().Properties.First(pr => pr.Name == propertyNamedArg.Name);
							var propertyName = new IdentifierExpression(propertyNamedArg.Name).WithAnnotation(propertyReference);
							var argumentValue = ConvertArgumentValue(propertyNamedArg.Argument);
							attribute.Arguments.Add(new AssignmentExpression(propertyName, argumentValue));
						}
					}

					if (customAttribute.HasFields) {
						foreach (var fieldNamedArg in customAttribute.Fields) {
							var fieldReference = customAttribute.AttributeType.Resolve().Fields.First(f => f.Name == fieldNamedArg.Name);
							var fieldName = new IdentifierExpression(fieldNamedArg.Name).WithAnnotation(fieldReference);
							var argumentValue = ConvertArgumentValue(fieldNamedArg.Argument);
							attribute.Arguments.Add(new AssignmentExpression(fieldName, argumentValue));
						}
					}
				}

				if (target == AttributeTarget.Module || target == AttributeTarget.Assembly) {
					// use separate section for each attribute
					foreach (var attribute in attributes) {
						var section = new AttributeSection();
						section.AttributeTarget = target;
						section.Attributes.Add(attribute);
						attributedNode.AddChild(section, AttributedNode.AttributeRole);
					}
				} else {
					// use single section for all attributes
					var section = new AttributeSection();
					section.AttributeTarget = target;
					section.Attributes.AddRange(attributes);
					attributedNode.AddChild(section, AttributedNode.AttributeRole);
				}
			}
		}

		private static Expression ConvertArgumentValue(CustomAttributeArgument parameter)
		{
			var type = parameter.Type.Resolve();
			Expression parameterValue;
			if (type.IsEnum)
			{
				parameterValue = MakePrimitive(Convert.ToInt64(parameter.Value), type);
			}
			else if (parameter.Value is TypeReference)
			{
				parameterValue = new TypeOfExpression()
				{
					Type = ConvertType((TypeReference)parameter.Value),
				};
			}
			else
			{
				parameterValue = new PrimitiveExpression(parameter.Value);
			}
			return parameterValue;
		}


		internal static Expression MakePrimitive(long val, TypeReference type)
		{
			if (TypeAnalysis.IsBoolean(type) && val == 0)
				return new Ast.PrimitiveExpression(false);
			else if (TypeAnalysis.IsBoolean(type) && val == 1)
				return new Ast.PrimitiveExpression(true);
			if (type != null)
			{ // cannot rely on type.IsValueType, it's not set for typerefs (but is set for typespecs)
				TypeDefinition enumDefinition = type.Resolve();
				if (enumDefinition != null && enumDefinition.IsEnum)
				{
					foreach (FieldDefinition field in enumDefinition.Fields)
					{
						if (field.IsStatic && object.Equals(CSharpPrimitiveCast.Cast(TypeCode.Int64, field.Constant, false), val))
							return ConvertType(enumDefinition).Member(field.Name).WithAnnotation(field);
						else if (!field.IsStatic && field.IsRuntimeSpecialName)
							type = field.FieldType; // use primitive type of the enum
					}
					if (IsFlagsEnum(enumDefinition))
					{
						long enumValue = val;
						Expression expr = null;
						foreach (FieldDefinition field in enumDefinition.Fields.Where(fld => fld.IsStatic))
						{
							long fieldValue = (long)CSharpPrimitiveCast.Cast(TypeCode.Int64, field.Constant, false);
							if (fieldValue == 0)
								continue;	// skip None enum value

							if ((fieldValue & enumValue) == fieldValue)
							{
								var fieldExpression = ConvertType(enumDefinition).Member(field.Name).WithAnnotation(field);
								if (expr == null)
									expr = fieldExpression;
								else
									expr = new BinaryOperatorExpression(expr, BinaryOperatorType.BitwiseOr, fieldExpression);

								enumValue &= ~fieldValue;
								if (enumValue == 0)
									break;
							}
						}
						if(enumValue == 0 && expr != null)
							return expr;
					}
					TypeCode enumBaseTypeCode = TypeAnalysis.GetTypeCode(type);
					return new Ast.PrimitiveExpression(CSharpPrimitiveCast.Cast(enumBaseTypeCode, val, false)).CastTo(ConvertType(enumDefinition));
				}
			}
			TypeCode code = TypeAnalysis.GetTypeCode(type);
			if (code == TypeCode.Object)
				return new Ast.PrimitiveExpression((int)val);
			else
				return new Ast.PrimitiveExpression(CSharpPrimitiveCast.Cast(code, val, false));
		}

		static bool IsFlagsEnum(TypeDefinition type)
		{
			if (!type.HasCustomAttributes)
				return false;

			return type.CustomAttributes.Any(attr => attr.AttributeType.FullName == "System.FlagsAttribute");
		}
	}
}
