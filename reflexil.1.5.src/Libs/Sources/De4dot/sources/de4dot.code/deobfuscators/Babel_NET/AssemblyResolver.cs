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

using System.IO;
using DeMono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class AssemblyResolver {
		ModuleDefinition module;
		TypeDefinition resolverType;
		MethodDefinition registerMethod;
		EmbeddedResource encryptedResource;
		EmbeddedAssemblyInfo[] embeddedAssemblyInfos = new EmbeddedAssemblyInfo[0];

		public class EmbeddedAssemblyInfo {
			public string fullname;
			public string extension;
			public byte[] data;

			public EmbeddedAssemblyInfo(string fullName, string extension, byte[] data) {
				this.fullname = fullName;
				this.extension = extension;
				this.data = data;
			}
		}

		public bool Detected {
			get { return resolverType != null; }
		}

		public TypeDefinition Type {
			get { return resolverType; }
		}

		public MethodDefinition InitMethod {
			get { return registerMethod; }
		}

		public EmbeddedResource EncryptedResource {
			get { return encryptedResource; }
		}

		public EmbeddedAssemblyInfo[] EmbeddedAssemblyInfos {
			get { return embeddedAssemblyInfos; }
		}

		public AssemblyResolver(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			var requiredTypes = new string[] {
				"System.Object",
				"System.Int32",
				"System.Collections.Hashtable",
			};
			foreach (var type in module.Types) {
				if (type.HasEvents)
					continue;
				if (!new FieldTypes(type).exactly(requiredTypes))
					continue;

				MethodDefinition regMethod, handler;
				if (!BabelUtils.findRegisterMethod(type, out regMethod, out handler))
					continue;

				resolverType = type;
				registerMethod = regMethod;
				return;
			}
		}

		public void initialize(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (resolverType == null)
				return;

			encryptedResource = BabelUtils.findEmbeddedResource(module, resolverType, simpleDeobfuscator, deob);
			if (encryptedResource == null) {
				Log.w("Could not find embedded assemblies resource");
				return;
			}

			var decrypted = new ResourceDecrypter(module).decrypt(encryptedResource.GetResourceData());
			var reader = new BinaryReader(new MemoryStream(decrypted));
			int numAssemblies = reader.ReadInt32();
			embeddedAssemblyInfos = new EmbeddedAssemblyInfo[numAssemblies];
			for (int i = 0; i < numAssemblies; i++) {
				string name = reader.ReadString();
				var data = reader.ReadBytes(reader.ReadInt32());
				var mod = ModuleDefinition.ReadModule(new MemoryStream(data));
				embeddedAssemblyInfos[i] = new EmbeddedAssemblyInfo(name, DeobUtils.getExtension(mod.Kind), data);
			}
		}
	}
}
