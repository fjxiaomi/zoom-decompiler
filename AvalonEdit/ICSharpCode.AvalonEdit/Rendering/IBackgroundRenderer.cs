﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Windows.Media;

namespace ICSharpCode.AvalonEdit.Rendering
{
	/// <summary>
	/// Background renderers draw in the background of a known layer.
	/// You can use background renderers to draw non-interactive elements on the TextView
	/// without introducing new UIElements.
	/// </summary>
	/// <remarks>Background renderer will draw only if their associated known
	/// layer chooses to draw them. For example, background renderers in the caret
	/// layer will be invisible when the caret is hidden.</remarks>
	public interface IBackgroundRenderer
	{
		/// <summary>
		/// Gets the layer on which this background renderer should draw.
		/// </summary>
		KnownLayer Layer { get; }
		
		/// <summary>
		/// Causes the background renderer to draw.
		/// </summary>
		void Draw(TextView textView, DrawingContext drawingContext);
	}
}
