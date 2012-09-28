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

using System;
using System.Collections.Generic;
using DeMono.Cecil;
using DeMono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code {
	// A simple class that statically detects the values of some local variables
	class VariableValues {
		IList<Block> allBlocks;
		IList<VariableDefinition> locals;
		Dictionary<VariableDefinition, Variable> variableToValue = new Dictionary<VariableDefinition, Variable>();

		public class Variable {
			int writes = 0;
			object value;
			bool unknownValue = false;

			public bool isValid() {
				return !unknownValue && writes == 1;
			}

			public object Value {
				get {
					if (!isValid())
						throw new ApplicationException("Unknown variable value");
					return value;
				}
				set { this.value = value; }
			}

			public void addWrite() {
				writes++;
			}

			public void setUnknown() {
				unknownValue = true;
			}
		}

		public VariableValues(IList<VariableDefinition> locals, IList<Block> allBlocks) {
			this.locals = locals;
			this.allBlocks = allBlocks;
			init();
		}

		void init() {
			foreach (var variable in locals)
				variableToValue[variable] = new Variable();

			foreach (var block in allBlocks) {
				for (int i = 0; i < block.Instructions.Count; i++) {
					var instr = block.Instructions[i];

					switch (instr.OpCode.Code) {
					case Code.Stloc:
					case Code.Stloc_S:
					case Code.Stloc_0:
					case Code.Stloc_1:
					case Code.Stloc_2:
					case Code.Stloc_3:
						var variable = Instr.getLocalVar(locals, instr);
						var val = variableToValue[variable];
						val.addWrite();
						object obj;
						if (!getValue(block, i, out obj))
							val.setUnknown();
						val.Value = obj;
						break;

					default:
						break;
					}
				}
			}
		}

		bool getValue(Block block, int index, out object obj) {
			while (true) {
				if (index <= 0) {
					obj = null;
					return false;
				}
				var instr = block.Instructions[--index];
				if (instr.OpCode == OpCodes.Nop)
					continue;

				switch (instr.OpCode.Code) {
				case Code.Ldc_I4:
				case Code.Ldc_I8:
				case Code.Ldc_R4:
				case Code.Ldc_R8:
				case Code.Ldstr:
					obj = instr.Operand;
					return true;
				case Code.Ldc_I4_S:
					obj = (int)(sbyte)instr.Operand;
					return true;

				case Code.Ldc_I4_0: obj = 0; return true;
				case Code.Ldc_I4_1: obj = 1; return true;
				case Code.Ldc_I4_2: obj = 2; return true;
				case Code.Ldc_I4_3: obj = 3; return true;
				case Code.Ldc_I4_4: obj = 4; return true;
				case Code.Ldc_I4_5: obj = 5; return true;
				case Code.Ldc_I4_6: obj = 6; return true;
				case Code.Ldc_I4_7: obj = 7; return true;
				case Code.Ldc_I4_8: obj = 8; return true;
				case Code.Ldc_I4_M1:obj = -1; return true;
				case Code.Ldnull:	obj = null; return true;

				default:
					obj = null;
					return false;
				}
			}
		}

		public Variable getValue(VariableDefinition variable) {
			return variableToValue[variable];
		}
	}

	abstract class MethodReturnValueInliner {
		protected List<CallResult> callResults;
		List<Block> allBlocks;
		Blocks blocks;
		VariableValues variableValues;
		int errors = 0;

		protected class CallResult {
			public Block block;
			public int callStartIndex;
			public int callEndIndex;
			public object[] args;
			public object returnValue;

			public CallResult(Block block, int callEndIndex) {
				this.block = block;
				this.callEndIndex = callEndIndex;
			}

			public MethodReference getMethodReference() {
				return (MethodReference)block.Instructions[callEndIndex].Operand;
			}
		}

		public bool InlinedAllCalls {
			get { return errors == 0; }
		}

		public abstract bool HasHandlers { get; }

		protected MethodDefinition Method {
			get { return blocks.Method; }
		}

		protected abstract void inlineAllCalls();

		// Returns null if method is not a method we should inline
		protected abstract CallResult createCallResult(MethodReference method, Block block, int callInstrIndex);

		public int decrypt(Blocks theBlocks) {
			if (!HasHandlers)
				return 0;
			try {
				blocks = theBlocks;
				callResults = new List<CallResult>();
				allBlocks = new List<Block>(blocks.MethodBlocks.getAllBlocks());

				findAllCallResults();
				inlineAllCalls();
				inlineReturnValues();
				return callResults.Count;
			}
			finally {
				blocks = null;
				callResults = null;
				allBlocks = null;
				variableValues = null;
			}
		}

		void getLocalVariableValue(VariableDefinition variable, out object value) {
			if (variableValues == null)
				variableValues = new VariableValues(blocks.Locals, allBlocks);
			var val = variableValues.getValue(variable);
			if (!val.isValid())
				throw new ApplicationException("Could not get value of local variable");
			value = val.Value;
		}

		void findAllCallResults() {
			foreach (var block in allBlocks)
				findCallResults(block);
		}

		void findCallResults(Block block) {
			for (int i = 0; i < block.Instructions.Count; i++) {
				var instr = block.Instructions[i];
				if (instr.OpCode != OpCodes.Call)
					continue;
				var method = instr.Operand as MethodReference;
				if (method == null)
					continue;

				var callResult = createCallResult(method, block, i);
				if (callResult == null)
					continue;

				if (findArgs(callResult))
					callResults.Add(callResult);
			}
		}

		bool findArgs(CallResult callResult) {
			var block = callResult.block;
			var method = callResult.getMethodReference();
			var methodArgs = DotNetUtils.getParameters(method);
			int numArgs = methodArgs.Count;
			var args = new object[numArgs];

			int instrIndex = callResult.callEndIndex - 1;
			for (int i = numArgs - 1; i >= 0; i--) {
				object arg = null;
				if (!getArg(method, block, ref arg, ref instrIndex))
					return false;
				if (arg is int)
					arg = fixIntArg(methodArgs[i].ParameterType, (int)arg);
				args[i] = arg;
			}

			callResult.args = args;
			callResult.callStartIndex = instrIndex + 1;
			return true;
		}

		object fixIntArg(TypeReference type, int value) {
			if (type.IsPrimitive) {
				switch (type.FullName) {
				case "System.Boolean":	return value != 0;
				case "System.Char":		return (char)value;
				case "System.Byte":		return (byte)value;
				case "System.SByte":	return (sbyte)value;
				case "System.Int16":	return (short)value;
				case "System.UInt16":	return (ushort)value;
				case "System.Int32":	return (int)value;
				case "System.UInt32":	return (uint)value;
				}
			}
			throw new ApplicationException(string.Format("Wrong type {0}", type));
		}

		bool getArg(MethodReference method, Block block, ref object arg, ref int instrIndex) {
			while (true) {
				if (instrIndex < 0) {
					// We're here if there were no cflow deobfuscation, or if there are two or
					// more blocks branching to the decrypter method, or the two blocks can't be
					// merged because one is outside the exception handler (eg. buggy obfuscator).
					Log.w("Could not find all arguments to method {0} ({1:X8})",
								Utils.removeNewlines(method),
								method.MetadataToken.ToInt32());
					errors++;
					return false;
				}

				var instr = block.Instructions[instrIndex--];
				switch (instr.OpCode.Code) {
				case Code.Ldc_I4:
				case Code.Ldc_I8:
				case Code.Ldc_R4:
				case Code.Ldc_R8:
				case Code.Ldstr:
					arg = instr.Operand;
					break;
				case Code.Ldc_I4_S:
					arg = (int)(sbyte)instr.Operand;
					break;

				case Code.Ldc_I4_0: arg = 0; break;
				case Code.Ldc_I4_1: arg = 1; break;
				case Code.Ldc_I4_2: arg = 2; break;
				case Code.Ldc_I4_3: arg = 3; break;
				case Code.Ldc_I4_4: arg = 4; break;
				case Code.Ldc_I4_5: arg = 5; break;
				case Code.Ldc_I4_6: arg = 6; break;
				case Code.Ldc_I4_7: arg = 7; break;
				case Code.Ldc_I4_8: arg = 8; break;
				case Code.Ldc_I4_M1:arg = -1; break;
				case Code.Ldnull:	arg = null; break;

				case Code.Nop:
					continue;

				case Code.Ldloc:
				case Code.Ldloc_S:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
					getLocalVariableValue(Instr.getLocalVar(blocks.Locals, instr), out arg);
					break;

				case Code.Ldsfld:
					arg = instr.Operand;
					break;

				default:
					Log.w("Could not find all arguments to method {0} ({1:X8}), instr: {2}",
								Utils.removeNewlines(method),
								method.MetadataToken.ToInt32(),
								instr);
					errors++;
					return false;
				}
				break;
			}

			return true;
		}

		void inlineReturnValues() {
			callResults.Sort((a, b) => {
				int i1 = allBlocks.FindIndex((x) => a.block == x);
				int i2 = allBlocks.FindIndex((x) => b.block == x);
				if (i1 < i2) return -1;
				if (i1 > i2) return 1;

				return Utils.compareInt32(a.callStartIndex, b.callStartIndex);
			});
			callResults.Reverse();
			inlineReturnValues(callResults);
		}

		protected abstract void inlineReturnValues(IList<CallResult> callResults);
	}
}
