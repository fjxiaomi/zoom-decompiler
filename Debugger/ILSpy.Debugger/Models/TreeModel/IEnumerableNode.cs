﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)
using System;
using Debugger.MetaData;
using ICSharpCode.NRefactory.CSharp;
using ILSpy.Debugger.Services.Debugger;

namespace ILSpy.Debugger.Models.TreeModel
{
	/// <summary>
	/// IEnumerable node in the variable tree.
	/// </summary>
	public class IEnumerableNode : TreeNode
	{
		Expression targetObject;
		Expression debugListExpression;
		
		public IEnumerableNode(Expression targetObject, DebugType itemType)
		{
			this.targetObject = targetObject;
			
			this.Name = "IEnumerable";
			this.Text = "Expanding will enumerate the IEnumerable";
			DebugType debugListType;
			this.debugListExpression = DebuggerHelpers.CreateDebugListExpression(targetObject, itemType, out debugListType);
			this.ChildNodes = Utils.LazyGetItemsOfIList(this.debugListExpression);
		}
	}
}
