﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Decompiler;
using Decompiler.Transforms;
using ICSharpCode.Decompiler;
using ICSharpCode.NRefactory.CSharp;
using Mono.Cecil;

namespace ICSharpCode.ILSpy
{
	/// <summary>
	/// Decompiler logic for C#.
	/// </summary>
	public class CSharpLanguage : Language
	{
		string name = "C#";
		bool showAllMembers;
		Predicate<IAstTransform> transformAbortCondition = null;
		
		public CSharpLanguage()
		{
		}
		
		#if DEBUG
		internal static IEnumerable<CSharpLanguage> GetDebugLanguages()
		{
			string lastTransformName = "no transforms";
			foreach (Type _transformType in TransformationPipeline.CreatePipeline(new DecompilerContext()).Select(v => v.GetType()).Distinct()) {
				Type transformType = _transformType; // copy for lambda
				yield return new CSharpLanguage {
					transformAbortCondition = v => transformType.IsInstanceOfType(v),
					name = "C# - " + lastTransformName,
					showAllMembers = true
				};
				lastTransformName = "after " + transformType.Name;
			}
			yield return new CSharpLanguage {
				name = "C# - " + lastTransformName,
				showAllMembers = true
			};
		}
		#endif
		
		public override string Name {
			get { return name; }
		}
		
		public override string FileExtension {
			get { return ".cs"; }
		}
		
		public override string ProjectFileExtension {
			get { return ".csproj"; }
		}
		
		public override void DecompileMethod(MethodDefinition method, ITextOutput output, DecompilationOptions options)
		{
			AstBuilder codeDomBuilder = CreateAstBuilder(options, method.DeclaringType);
			codeDomBuilder.AddMethod(method);
			codeDomBuilder.GenerateCode(output, transformAbortCondition);
		}
		
		public override void DecompileProperty(PropertyDefinition property, ITextOutput output, DecompilationOptions options)
		{
			AstBuilder codeDomBuilder = CreateAstBuilder(options, property.DeclaringType);
			codeDomBuilder.AddProperty(property);
			codeDomBuilder.GenerateCode(output, transformAbortCondition);
		}
		
		public override void DecompileField(FieldDefinition field, ITextOutput output, DecompilationOptions options)
		{
			AstBuilder codeDomBuilder = CreateAstBuilder(options, field.DeclaringType);
			codeDomBuilder.AddField(field);
			codeDomBuilder.GenerateCode(output, transformAbortCondition);
		}
		
		public override void DecompileEvent(EventDefinition ev, ITextOutput output, DecompilationOptions options)
		{
			AstBuilder codeDomBuilder = CreateAstBuilder(options, ev.DeclaringType);
			codeDomBuilder.AddEvent(ev);
			codeDomBuilder.GenerateCode(output, transformAbortCondition);
		}
		
		public override void DecompileType(TypeDefinition type, ITextOutput output, DecompilationOptions options)
		{
			AstBuilder codeDomBuilder = CreateAstBuilder(options, type);
			codeDomBuilder.AddType(type);
			codeDomBuilder.GenerateCode(output, transformAbortCondition);
		}
		
		public override void DecompileAssembly(AssemblyDefinition assembly, string fileName, ITextOutput output, DecompilationOptions options)
		{
			if (options.FullDecompilation) {
				if (options.SaveAsProjectDirectory != null) {
					var files = WriteFilesInProject(assembly, options);
					WriteProjectFile(new TextOutputWriter(output), files, assembly.MainModule);
				} else {
					foreach (TypeDefinition type in assembly.MainModule.Types) {
						AstBuilder codeDomBuilder = CreateAstBuilder(options, type);
						codeDomBuilder.AddType(type);
						codeDomBuilder.GenerateCode(output, transformAbortCondition);
						output.WriteLine();
					}
				}
			} else {
				base.DecompileAssembly(assembly, fileName, output, options);
			}
		}
		
		void WriteProjectFile(TextWriter writer, IEnumerable<string> files, ModuleDefinition module)
		{
			const string ns = "http://schemas.microsoft.com/developer/msbuild/2003";
			string platformName;
			switch (module.Architecture) {
				case TargetArchitecture.I386:
					if ((module.Attributes & ModuleAttributes.Required32Bit) == ModuleAttributes.Required32Bit)
						platformName = "x86";
					else
						platformName = "AnyCPU";
					break;
				case TargetArchitecture.AMD64:
					platformName = "x64";
					break;
				case TargetArchitecture.IA64:
					platformName = "Itanium";
					break;
				default:
					throw new NotSupportedException("Invalid value for TargetArchitecture");
			}
			using (XmlTextWriter w = new XmlTextWriter(writer)) {
				w.Formatting = Formatting.Indented;
				w.WriteStartDocument();
				w.WriteStartElement("Project", ns);
				w.WriteAttributeString("ToolsVersion", "4.0");
				w.WriteAttributeString("DefaultTargets", "Build");
				
				w.WriteStartElement("PropertyGroup");
				w.WriteElementString("ProjectGuid", Guid.NewGuid().ToString().ToUpperInvariant());
				
				w.WriteStartElement("Configuration");
				w.WriteAttributeString("Condition", " '$(Configuration)' == '' ");
				w.WriteValue("Debug");
				w.WriteEndElement(); // </Configuration>
				
				w.WriteStartElement("Platform");
				w.WriteAttributeString("Condition", " '$(Platform)' == '' ");
				w.WriteValue(platformName);
				w.WriteEndElement(); // </Platform>
				
				switch (module.Kind) {
					case ModuleKind.Windows:
						w.WriteElementString("OutputType", "WinExe");
						break;
					case ModuleKind.Console:
						w.WriteElementString("OutputType", "Exe");
						break;
					default:
						w.WriteElementString("OutputType", "Library");
						break;
				}
				
				w.WriteElementString("AssemblyName", module.Assembly.Name.Name);
				switch (module.Runtime) {
					case TargetRuntime.Net_1_0:
						w.WriteElementString("TargetFrameworkVersion", "v1.0");
						break;
					case TargetRuntime.Net_1_1:
						w.WriteElementString("TargetFrameworkVersion", "v1.1");
						break;
					case TargetRuntime.Net_2_0:
						w.WriteElementString("TargetFrameworkVersion", "v2.0");
						// TODO: Detect when .NET 3.0/3.5 is required
						break;
					default:
						w.WriteElementString("TargetFrameworkVersion", "v4.0");
						// TODO: Detect TargetFrameworkProfile
						break;
				}
				w.WriteElementString("WarningLevel", "4");
				
				w.WriteEndElement(); // </PropertyGroup>
				
				w.WriteStartElement("PropertyGroup"); // platform-specific
				w.WriteAttributeString("Condition", " '$(Platform)' == '" + platformName + "' ");
				w.WriteElementString("PlatformTarget", platformName);
				w.WriteEndElement(); // </PropertyGroup> (platform-specific)
				
				w.WriteStartElement("PropertyGroup"); // Debug
				w.WriteAttributeString("Condition", " '$(Configuration)' == 'Debug' ");
				w.WriteElementString("OutputPath", "bin\\Debug\\");
				w.WriteElementString("DebugSymbols", "true");
				w.WriteElementString("DebugType", "full");
				w.WriteElementString("Optimize", "false");
				w.WriteEndElement(); // </PropertyGroup> (Debug)
				
				w.WriteStartElement("PropertyGroup"); // Release
				w.WriteAttributeString("Condition", " '$(Configuration)' == 'Release' ");
				w.WriteElementString("OutputPath", "bin\\Release\\");
				w.WriteElementString("DebugSymbols", "true");
				w.WriteElementString("DebugType", "pdbonly");
				w.WriteElementString("Optimize", "true");
				w.WriteEndElement(); // </PropertyGroup> (Release)
				
				
				w.WriteStartElement("ItemGroup"); // References
				foreach (AssemblyNameReference r in module.AssemblyReferences) {
					if (r.Name != "mscorlib") {
						w.WriteStartElement("Reference");
						w.WriteAttributeString("Include", r.Name);
						// TODO: RequiredTargetFramework
						w.WriteEndElement();
					}
				}
				w.WriteEndElement(); // </ItemGroup> (References)
				
				w.WriteStartElement("ItemGroup"); // Code
				foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
					w.WriteStartElement("Compile");
					w.WriteAttributeString("Include", file);
					w.WriteEndElement();
				}
				w.WriteEndElement();
				
				w.WriteStartElement("Import");
				w.WriteAttributeString("Project", "$(MSBuildToolsPath)\\Microsoft.CSharp.targets");
				w.WriteEndElement();
				
				w.WriteEndDocument();
			}
		}
		
		IEnumerable<string> WriteFilesInProject(AssemblyDefinition assembly, DecompilationOptions options)
		{
			var files = assembly.MainModule.Types.Where(t => t.Name != "<Module>").GroupBy(
				delegate (TypeDefinition type) {
					string file = TextView.DecompilerTextView.CleanUpName(type.Name) + this.FileExtension;
					if (string.IsNullOrEmpty(type.Namespace)) {
						return file;
					} else {
						string dir = TextView.DecompilerTextView.CleanUpName(type.Namespace);
						Directory.CreateDirectory(Path.Combine(options.SaveAsProjectDirectory, dir));
						return Path.Combine(dir, file);
					}
				}, StringComparer.OrdinalIgnoreCase).ToList();
			
			AstMethodBodyBuilder.ClearUnhandledOpcodes();
			Parallel.ForEach(
				files,
				new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
				delegate (IGrouping<string, TypeDefinition> file) {
					using (StreamWriter w = new StreamWriter(Path.Combine(options.SaveAsProjectDirectory, file.Key))) {
						AstBuilder codeDomBuilder = CreateAstBuilder(options, null);
						foreach (TypeDefinition type in file)
							codeDomBuilder.AddType(type);
						codeDomBuilder.GenerateCode(new PlainTextOutput(w), transformAbortCondition);
					}
				});
			AstMethodBodyBuilder.PrintNumberOfUnhandledOpcodes();
			return files.Select(f => f.Key);
		}
		
		AstBuilder CreateAstBuilder(DecompilationOptions options, TypeDefinition currentType)
		{
			return new AstBuilder(
				new DecompilerContext {
					CancellationToken = options.CancellationToken,
					CurrentType = currentType
				});
		}
		
		public override string TypeToString(TypeReference type, bool includeNamespace, ICustomAttributeProvider typeAttributes)
		{
			AstType astType = AstBuilder.ConvertType(type, typeAttributes);
			if (!includeNamespace) {
				var tre = new TypeReferenceExpression { Type = astType };
				tre.AcceptVisitor(new RemoveNamespaceFromType(), null);
				astType = tre.Type;
			}
			
			StringWriter w = new StringWriter();
			if (type.IsByReference) {
				ParameterDefinition pd = typeAttributes as ParameterDefinition;
				if (pd != null && (!pd.IsIn && pd.IsOut))
					w.Write("out ");
				else
					w.Write("ref ");
			}
			
			astType.AcceptVisitor(new OutputVisitor(w, new CSharpFormattingPolicy()), null);
			return w.ToString();
		}
		
		sealed class RemoveNamespaceFromType : DepthFirstAstVisitor<object, object>
		{
			public override object VisitMemberType(MemberType memberType, object data)
			{
				base.VisitMemberType(memberType, data);
				SimpleType st = memberType.Target as SimpleType;
				if (st != null && !st.TypeArguments.Any()) {
					SimpleType newSt = new SimpleType(memberType.MemberName);
					memberType.TypeArguments.MoveTo(newSt.TypeArguments);
					memberType.ReplaceWith(newSt);
				}
				return null;
			}
		}
		
		public override bool ShowMember(MemberReference member)
		{
			return showAllMembers || !AstBuilder.MemberIsHidden(member);
		}
	}
}
