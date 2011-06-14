#region Copyright
//
// GenericParameter.cs
//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2011 Jb Evain
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
#endregion

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using Mi.Assemblies.Metadata;

namespace Mi.Assemblies {

	public sealed class GenericParameter : TypeReference, ICustomAttributeProvider {

		readonly IGenericParameterProvider owner;

		ushort attributes;
		List<TypeReference> constraints;
		List<CustomAttribute> custom_attributes;

		public GenericParameterAttributes Attributes {
			get { return (GenericParameterAttributes) attributes; }
			set { attributes = (ushort) value; }
		}

		public int Position {
			get {
				if (owner == null)
					return -1;

				return owner.GenericParameters.IndexOf (this);
			}
		}

		public IGenericParameterProvider Owner {
			get { return owner; }
		}

		public bool HasConstraints {
			get {
				if (constraints != null)
					return constraints.Count > 0;

				if (HasImage)
					return Module.Read (this, (generic_parameter, reader) => reader.HasGenericConstraints (generic_parameter));

				return false;
			}
		}

		public List<TypeReference> Constraints {
			get {
				if (constraints != null)
					return constraints;

				if (HasImage)
					return Module.Read (ref constraints, this, (generic_parameter, reader) => reader.ReadGenericConstraints (generic_parameter));

				return constraints = new List<TypeReference> ();
			}
		}

		public bool HasCustomAttributes {
			get {
				if (custom_attributes != null)
					return custom_attributes.Count > 0;

				return this.GetHasCustomAttributes (Module);
			}
		}

		public List<CustomAttribute> CustomAttributes {
			get { return custom_attributes ?? (this.GetCustomAttributes (ref custom_attributes, Module)); }
		}

		public override IMetadataScope Scope {
			get {
				if (owner.GenericParameterType == GenericParameterType.Method)
					return ((MethodReference) owner).DeclaringType.Scope;

				return ((TypeReference) owner).Scope;
			}
		}

		public override ModuleDefinition Module {
			get { return ((MemberReference) owner).Module; }
		}

		public override string Name {
			get {
				if (!string.IsNullOrEmpty (base.Name))
					return base.Name;

				return base.Name = (owner.GenericParameterType == GenericParameterType.Type ? "!" : "!!") + Position;
			}
		}

		public override string Namespace {
			get { return string.Empty; }
			set { throw new InvalidOperationException (); }
		}

		public override string FullName {
			get { return Name; }
		}

		public override bool IsGenericParameter {
			get { return true; }
		}

		internal override bool ContainsGenericParameter {
			get { return true; }
		}

		public override MetadataType MetadataType {
			get { return (MetadataType) etype; }
		}

		#region GenericParameterAttributes

		public bool IsNonVariant {
			get { return attributes.GetMaskedAttributes ((ushort) GenericParameterAttributes.VarianceMask, (ushort) GenericParameterAttributes.NonVariant); }
			set { attributes = attributes.SetMaskedAttributes ((ushort) GenericParameterAttributes.VarianceMask, (ushort) GenericParameterAttributes.NonVariant, value); }
		}

		public bool IsCovariant {
			get { return attributes.GetMaskedAttributes ((ushort) GenericParameterAttributes.VarianceMask, (ushort) GenericParameterAttributes.Covariant); }
			set { attributes = attributes.SetMaskedAttributes ((ushort) GenericParameterAttributes.VarianceMask, (ushort) GenericParameterAttributes.Covariant, value); }
		}

		public bool IsContravariant {
			get { return attributes.GetMaskedAttributes ((ushort) GenericParameterAttributes.VarianceMask, (ushort) GenericParameterAttributes.Contravariant); }
			set { attributes = attributes.SetMaskedAttributes ((ushort) GenericParameterAttributes.VarianceMask, (ushort) GenericParameterAttributes.Contravariant, value); }
		}

		public bool HasReferenceTypeConstraint {
			get { return attributes.GetAttributes ((ushort) GenericParameterAttributes.ReferenceTypeConstraint); }
			set { attributes = attributes.SetAttributes ((ushort) GenericParameterAttributes.ReferenceTypeConstraint, value); }
		}

		public bool HasNotNullableValueTypeConstraint {
			get { return attributes.GetAttributes ((ushort) GenericParameterAttributes.NotNullableValueTypeConstraint); }
			set { attributes = attributes.SetAttributes ((ushort) GenericParameterAttributes.NotNullableValueTypeConstraint, value); }
		}

		public bool HasDefaultConstructorConstraint {
			get { return attributes.GetAttributes ((ushort) GenericParameterAttributes.DefaultConstructorConstraint); }
			set { attributes = attributes.SetAttributes ((ushort) GenericParameterAttributes.DefaultConstructorConstraint, value); }
		}

		#endregion

		public GenericParameter (IGenericParameterProvider owner)
			: this (string.Empty, owner)
		{
		}

		public GenericParameter (string name, IGenericParameterProvider owner)
			: base (string.Empty, name)
		{
			if (owner == null)
				throw new ArgumentNullException ();

			this.owner = owner;
			this.etype = owner.GenericParameterType == GenericParameterType.Type ? ElementType.Var : ElementType.MVar;
		}

		public override TypeDefinition Resolve ()
		{
			return null;
		}
	}
}
