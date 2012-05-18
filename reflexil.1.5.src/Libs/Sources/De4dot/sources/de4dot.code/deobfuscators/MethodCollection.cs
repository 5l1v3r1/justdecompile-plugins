﻿/*
    Copyright (C) 2011-2012 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using DeMono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	class MethodCollection {
		TypeDefinitionDict<bool> types = new TypeDefinitionDict<bool>();
		MethodDefinitionAndDeclaringTypeDict<bool> methods = new MethodDefinitionAndDeclaringTypeDict<bool>();

		public bool exists(MethodReference method) {
			if (method == null)
				return false;
			if (method.DeclaringType != null && types.find(method.DeclaringType))
				return true;
			return methods.find(method);
		}

		public void add(MethodDefinition method) {
			methods.add(method, true);
		}

		public void add(IEnumerable<MethodDefinition> methods) {
			foreach (var method in methods)
				add(method);
		}

		public void add(TypeDefinition type) {
			types.add(type, true);
		}

		public void addAndNested(TypeDefinition type) {
			foreach (var t in TypeDefinition.GetTypes(new List<TypeDefinition> { type }))
				add(type);
		}

		public void addAndNested(IList<TypeDefinition> types) {
			foreach (var type in TypeDefinition.GetTypes(types))
				add(type);
		}
	}
}
