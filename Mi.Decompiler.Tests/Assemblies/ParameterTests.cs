using System;
using System.Linq;

using Mi.Assemblies;
using Mi.Assemblies.Metadata;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mi.Decompiler.Tests;

namespace Mi.Assemblies.Tests {

	[TestClass]
	public class ParameterTests 
    {
		[TestMethod]
		public void MarshalAsI4 ()
		{
            var module = SampleInputLoader.LoadAssembly("marshal").MainModule;
			var bar = module.GetType ("Bar");
			var pan = bar.GetMethod ("Pan");

			Assert.AreEqual (1, pan.Parameters.Count);

			var parameter = pan.Parameters [0];

			Assert.IsTrue (parameter.HasMarshalInfo);
			var info = parameter.MarshalInfo;

			Assert.AreEqual (typeof (MarshalInfo), info.GetType ());
			Assert.AreEqual (NativeType.I4, info.NativeType);
		}

		[TestMethod]
        public void CustomMarshaler()
        {
            var module = SampleInputLoader.LoadAssembly("marshal").MainModule;
			var bar = module.GetType ("Bar");
			var pan = bar.GetMethod ("PanPan");

			var parameter = pan.Parameters [0];

			Assert.IsTrue (parameter.HasMarshalInfo);

			var info = (CustomMarshalInfo) parameter.MarshalInfo;

			Assert.AreEqual (Guid.Empty, info.Guid);
			Assert.AreEqual (string.Empty, info.UnmanagedType);
			Assert.AreEqual (NativeType.CustomMarshaler, info.NativeType);
			Assert.AreEqual ("nomnom", info.Cookie);

			Assert.AreEqual ("Boc", info.ManagedType.FullName);
			Assert.AreEqual (module, info.ManagedType.Scope);
		}

		[TestMethod]
        public void SafeArrayMarshaler()
        {
            var module = SampleInputLoader.LoadAssembly("marshal").MainModule;
			var bar = module.GetType ("Bar");
			var pan = bar.GetMethod ("PanPan");

			Assert.IsTrue (pan.MethodReturnType.HasMarshalInfo);

			var info = (SafeArrayMarshalInfo) pan.MethodReturnType.MarshalInfo;

			Assert.AreEqual (VariantType.Dispatch, info.ElementType);
		}

		[TestMethod]
        public void ArrayMarshaler()
        {
            var module = SampleInputLoader.LoadAssembly("marshal").MainModule;
			var bar = module.GetType ("Bar");
			var pan = bar.GetMethod ("PanPan");

			var parameter = pan.Parameters [1];

			Assert.IsTrue (parameter.HasMarshalInfo);

			var info = (ArrayMarshalInfo) parameter.MarshalInfo;

			Assert.AreEqual (NativeType.I8, info.ElementType);
			Assert.AreEqual (66, info.Size);
			Assert.AreEqual (2, info.SizeParameterIndex);

			parameter = pan.Parameters [3];

			Assert.IsTrue (parameter.HasMarshalInfo);

			info = (ArrayMarshalInfo) parameter.MarshalInfo;

			Assert.AreEqual (NativeType.I2, info.ElementType);
			Assert.AreEqual (-1, info.Size);
			Assert.AreEqual (-1, info.SizeParameterIndex);
		}

		[TestMethod]
        public void ArrayMarshalerSized()
        {
            var module = SampleInputLoader.LoadAssembly("marshal").MainModule;
			var delegate_type = module.GetType ("SomeMethod");
			var parameter = delegate_type.GetMethod ("Invoke").Parameters [1];

			Assert.IsTrue (parameter.HasMarshalInfo);
			var array_info = (ArrayMarshalInfo) parameter.MarshalInfo;

			Assert.IsNotNull (array_info);

			Assert.AreEqual (0, array_info.SizeParameterMultiplier);
		}

		[TestMethod ]
        public void BoxedDefaultArgumentValue()
        {
            var module = SampleInputLoader.LoadAssembly("boxedoptarg").MainModule;
			var foo = module.GetType ("Foo");
			var bar = foo.GetMethod ("Bar");
			var baz = bar.Parameters [0];

			Assert.IsTrue (baz.HasConstant);
			Assert.AreEqual (-1, baz.Constant);
		}

		[TestMethod]
		public void AddParameterIndex ()
		{
			var object_ref = new TypeReference ("System", "Object", null, null, false);
			var method = new MethodDefinition ("foo", MethodAttributes.Static, object_ref);

			var x = new ParameterDefinition ("x", ParameterAttributes.None, object_ref);
			var y = new ParameterDefinition ("y", ParameterAttributes.None, object_ref);

			method.Parameters.Add (x);
			method.Parameters.Add (y);

			Assert.AreEqual (0, x.Index);
			Assert.AreEqual (1, y.Index);

			Assert.AreEqual (method, x.Method);
			Assert.AreEqual (method, y.Method);
		}

		[TestMethod]
		public void RemoveAtParameterIndex ()
		{
			var object_ref = new TypeReference ("System", "Object", null, null, false);
			var method = new MethodDefinition ("foo", MethodAttributes.Static, object_ref);

			var x = new ParameterDefinition ("x", ParameterAttributes.None, object_ref);
			var y = new ParameterDefinition ("y", ParameterAttributes.None, object_ref);
			var z = new ParameterDefinition ("y", ParameterAttributes.None, object_ref);

			method.Parameters.Add (x);
			method.Parameters.Add (y);
			method.Parameters.Add (z);

			Assert.AreEqual (0, x.Index);
			Assert.AreEqual (1, y.Index);
			Assert.AreEqual (2, z.Index);

			method.Parameters.RemoveAt (1);

			Assert.AreEqual (0, x.Index);
			Assert.AreEqual (-1, y.Index);
			Assert.AreEqual (1, z.Index);
		}

		[TestMethod]
		public void RemoveParameterIndex ()
		{
			var object_ref = new TypeReference ("System", "Object", null, null, false);
			var method = new MethodDefinition ("foo", MethodAttributes.Static, object_ref);

			var x = new ParameterDefinition ("x", ParameterAttributes.None, object_ref);
			var y = new ParameterDefinition ("y", ParameterAttributes.None, object_ref);
			var z = new ParameterDefinition ("y", ParameterAttributes.None, object_ref);

			method.Parameters.Add (x);
			method.Parameters.Add (y);
			method.Parameters.Add (z);

			Assert.AreEqual (0, x.Index);
			Assert.AreEqual (1, y.Index);
			Assert.AreEqual (2, z.Index);

			method.Parameters.Remove (y);

			Assert.AreEqual (0, x.Index);
			Assert.AreEqual (-1, y.Index);
			Assert.AreEqual (1, z.Index);
		}

		[TestMethod]
		public void InsertParameterIndex ()
		{
			var object_ref = new TypeReference ("System", "Object", null, null, false);
			var method = new MethodDefinition ("foo", MethodAttributes.Static, object_ref);

			var x = new ParameterDefinition ("x", ParameterAttributes.None, object_ref);
			var y = new ParameterDefinition ("y", ParameterAttributes.None, object_ref);
			var z = new ParameterDefinition ("y", ParameterAttributes.None, object_ref);

			method.Parameters.Add (x);
			method.Parameters.Add (z);

			Assert.AreEqual (0, x.Index);
			Assert.AreEqual (-1, y.Index);
			Assert.AreEqual (1, z.Index);

			method.Parameters.Insert (1, y);

			Assert.AreEqual (0, x.Index);
			Assert.AreEqual (1, y.Index);
			Assert.AreEqual (2, z.Index);
		}

		[TestMethod]
        public void GenericParameterConstant()
        {
            var module = SampleInputLoader.LoadAssembly("hello").MainModule;
			var foo = module.GetType ("Foo");
			var method = foo.GetMethod ("GetState");

			Assert.IsNotNull (method);

			var parameter = method.Parameters [1];

			Assert.IsTrue (parameter.HasConstant);
			Assert.IsNull (parameter.Constant);
		}
	}
}
