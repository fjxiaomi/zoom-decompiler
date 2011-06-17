// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;

namespace MethodExplicit
{
	public interface IMyInterface
	{
		void MyMethod();
	}
	public class MyClass : IMyInterface
	{
		void IMyInterface.MyMethod()
		{
		}
	}
}
