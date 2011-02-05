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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.FlowAnalysis;
using ICSharpCode.TreeView;
using Microsoft.Win32;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace ICSharpCode.ILSpy
{
	/// <summary>
	/// The main window of the application.
	/// </summary>
	public partial class MainWindow : Window
	{
		AssemblyList assemblyList = new AssemblyList();
		FilterSettings filterSettings = new FilterSettings();
		ReferenceElementGenerator referenceElementGenerator;
		
		static readonly System.Reflection.Assembly[] initialAssemblies = {
			typeof(object).Assembly,
			typeof(Uri).Assembly,
			typeof(System.Linq.Enumerable).Assembly,
			typeof(System.Xml.XmlDocument).Assembly,
			typeof(System.Windows.Markup.MarkupExtension).Assembly,
			typeof(System.Windows.Rect).Assembly,
			typeof(System.Windows.UIElement).Assembly,
			typeof(System.Windows.FrameworkElement).Assembly,
			typeof(ICSharpCode.TreeView.SharpTreeView).Assembly,
			typeof(Mono.Cecil.AssemblyDefinition).Assembly,
			typeof(MainWindow).Assembly,
			typeof(ICSharpCode.Decompiler.GraphVizGraph).Assembly
		};
		
		public MainWindow()
		{
			referenceElementGenerator = new ReferenceElementGenerator(this);
			this.DataContext = filterSettings;
			InitializeComponent();
			
			languageComboBox.Items.Add(new Decompiler.CSharpLanguage());
			languageComboBox.Items.Add(new Disassembler.ILLanguage(false));
			languageComboBox.Items.Add(new Disassembler.ILLanguage(true));
			languageComboBox.SelectedItem = languageComboBox.Items[0];
			
			textEditor.Text = "Welcome to ILSpy!";
			
			textEditor.TextArea.TextView.ElementGenerators.Add(referenceElementGenerator);
			
			AssemblyListTreeNode assemblyListTreeNode = new AssemblyListTreeNode(assemblyList);
			assemblyListTreeNode.FilterSettings = filterSettings.Clone();
			filterSettings.PropertyChanged += delegate {
				// filterSettings is mutable; but the ILSpyTreeNode filtering assumes that filter settings are immutable.
				// Thus, the main window will use one mutable instance (for data-binding), and assign a new clone to the ILSpyTreeNodes whenever the main
				// mutable instance changes.
				assemblyListTreeNode.FilterSettings = filterSettings.Clone();
			};
			treeView.Root = assemblyListTreeNode;
			assemblyListTreeNode.Select = SelectNode;
			
			foreach (System.Reflection.Assembly asm in initialAssemblies)
				assemblyList.OpenAssembly(asm.Location);
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 1; i < args.Length; i++) {
				assemblyList.OpenAssembly(args[i]);
			}
			
			#if DEBUG
			toolBar.Items.Add(new Separator());
			
			Button cfg = new Button() { Content = "CFG" };
			cfg.Click += new RoutedEventHandler(cfg_Click);
			toolBar.Items.Add(cfg);
			
			Button ssa = new Button() { Content = "SSA" };
			ssa.Click += new RoutedEventHandler(ssa_Click);
			toolBar.Items.Add(ssa);
			
			Button varGraph = new Button() { Content = "Var" };
			varGraph.Click += new RoutedEventHandler(varGraph_Click);
			toolBar.Items.Add(varGraph);
			#endif
		}
		
		void SelectNode(SharpTreeNode obj)
		{
			if (obj != null) {
				foreach (SharpTreeNode node in obj.Ancestors())
					node.IsExpanded = true;
				
				treeView.SelectedItem = obj;
				treeView.ScrollIntoView(obj);
			}
		}
		
		#region Debugging CFG
		#if DEBUG
		void cfg_Click(object sender, RoutedEventArgs e)
		{
			MethodTreeNode node = treeView.SelectedItem as MethodTreeNode;
			if (node != null && node.MethodDefinition.HasBody) {
				var cfg = ControlFlowGraphBuilder.Build(node.MethodDefinition.Body);
				cfg.ComputeDominance();
				cfg.ComputeDominanceFrontier();
				ShowGraph(node.MethodDefinition.Name + "-cfg", cfg.ExportGraph());
			}
		}
		
		void ssa_Click(object sender, RoutedEventArgs e)
		{
			MethodTreeNode node = treeView.SelectedItem as MethodTreeNode;
			if (node != null && node.MethodDefinition.HasBody) {
				node.MethodDefinition.Body.SimplifyMacros();
				ShowGraph(node.MethodDefinition.Name + "-ssa", SsaFormBuilder.Build(node.MethodDefinition).ExportBlockGraph());
			}
		}
		
		void varGraph_Click(object sender, RoutedEventArgs e)
		{
			MethodTreeNode node = treeView.SelectedItem as MethodTreeNode;
			if (node != null && node.MethodDefinition.HasBody) {
				node.MethodDefinition.Body.SimplifyMacros();
				ShowGraph(node.MethodDefinition.Name + "-var", SsaFormBuilder.Build(node.MethodDefinition).ExportVariableGraph());
			}
		}
		
		void ShowGraph(string name, GraphVizGraph graph)
		{
			foreach (char c in Path.GetInvalidFileNameChars())
				name = name.Replace(c, '-');
			string fileName = Path.Combine(Path.GetTempPath(), name);
			graph.Save(fileName + ".gv");
			Process.Start("dot", "\"" + fileName + ".gv\" -Tpng -o \"" + fileName + ".png\"").WaitForExit();
			Process.Start(fileName + ".png");
		}
		#endif
		#endregion
		
		void OpenCommandExecuted(object sender, ExecutedRoutedEventArgs e)
		{
			e.Handled = true;
			OpenFileDialog dlg = new OpenFileDialog();
			dlg.Filter = ".NET assemblies|*.dll;*.exe|All files|*.*";
			dlg.Multiselect = true;
			dlg.RestoreDirectory = true;
			if (dlg.ShowDialog() == true) {
				OpenFiles(dlg.FileNames);
			}
		}
		
		void OpenFiles(string[] fileNames)
		{
			treeView.UnselectAll();
			SharpTreeNode lastNode = null;
			foreach (string file in fileNames) {
				var asm = assemblyList.OpenAssembly(file);
				if (asm != null) {
					treeView.SelectedItems.Add(asm);
					lastNode = asm;
				}
			}
			if (lastNode != null)
				treeView.ScrollIntoView(lastNode);
		}
		
		void ExitClick(object sender, RoutedEventArgs e)
		{
			Close();
		}
		
		void AboutClick(object sender, RoutedEventArgs e)
		{
			AboutDialog dlg = new AboutDialog();
			dlg.Owner = this;
			dlg.ShowDialog();
		}
		
		void OpenFromGac_Click(object sender, RoutedEventArgs e)
		{
			OpenFromGacDialog dlg = new OpenFromGacDialog();
			dlg.Owner = this;
			if (dlg.ShowDialog() == true) {
				OpenFiles(dlg.SelectedFileNames);
			}
		}
		
		SmartTextOutput textOutput;
		
		void TreeView_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			try {
				textEditor.SyntaxHighlighting = ILSpy.Language.Current.SyntaxHighlighting;
				textOutput = new SmartTextOutput();
				foreach (var node in treeView.SelectedItems.OfType<ILSpyTreeNode>()) {
					textOutput.CurrentTreeNode = node;
					node.Decompile(ILSpy.Language.Current, textOutput);
				}
				referenceElementGenerator.References = textOutput.References;
				textEditor.Text = textOutput.ToString();
			} catch (Exception ex) {
				textEditor.SyntaxHighlighting = null;
				referenceElementGenerator.References = null;
				textEditor.Text = ex.ToString();
			}
		}
		
		internal void JumpToReference(ReferenceSegment referenceSegment)
		{
			object reference = referenceSegment.Reference;
			if (textOutput != null) {
				int pos = textOutput.GetDefinitionPosition(reference);
				if (pos >= 0) {
					textEditor.TextArea.Focus();
					textEditor.Select(pos, 0);
					textEditor.ScrollTo(textEditor.TextArea.Caret.Line, textEditor.TextArea.Caret.Column);
					Dispatcher.Invoke(DispatcherPriority.Background, new Action(
						delegate {
							CaretHighlightAdorner.DisplayCaretHighlightAnimation(textEditor.TextArea);
						}));
					return;
				}
			}
			if (reference is TypeReference) {
				SelectNode(assemblyList.FindTypeNode(((TypeReference)reference).Resolve()));
			} else if (reference is MethodReference) {
				SelectNode(assemblyList.FindMethodNode(((MethodReference)reference).Resolve()));
			} else if (reference is FieldReference) {
				SelectNode(assemblyList.FindFieldNode(((FieldReference)reference).Resolve()));
			} else if (reference is PropertyReference) {
				SelectNode(assemblyList.FindPropertyNode(((PropertyReference)reference).Resolve()));
			} else if (reference is EventReference) {
				SelectNode(assemblyList.FindEventNode(((EventReference)reference).Resolve()));
			} else if (reference is AssemblyDefinition) {
				SelectNode(assemblyList.Assemblies.FirstOrDefault(node => node.AssemblyDefinition == reference));
			}
		}
		
		void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			ILSpy.Language.Current = (ILSpy.Language)languageComboBox.SelectedItem;
			TreeView_SelectionChanged(null, null);
		}
	}
}