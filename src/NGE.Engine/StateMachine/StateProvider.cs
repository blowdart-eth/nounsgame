﻿// Copyright © Conatus Creative, Inc. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE.md in the project root for license terms.

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace NGE.Engine.StateMachine
{
	/// <summary>Provides state infrastructure for implementing finite state machines with stateful methods.</summary>
	/// <remarks>
	///     This is loosely based on UnrealScript states. UnrealScript does its magic by chaining the VTable
	///     of objects on-the-fly (educated guess).
	///
	///		We can't do that in C#, so instead we maintain a secondary "VTable" for state methods (the `MethodTable`)
	///     that we setup using reflection.
	///
	///     We simply forward method calls to the method table manually.
	/// </remarks>
	public class StateProvider
    {
        private static List<State>? allStateInstances;
        private static Dictionary<Type, List<State>>? allStatesByType;

		/// <summary>Collection of all objects used by the state machine. These are definitions, for the purpose of serialization.</summary>
		public static List<State> AllStateInstances
		{
			get
			{
				Debug.Assert(allStateInstances != null);
				return allStateInstances;
			}
		}

		public interface IHaveSymbol
		{
			string Symbol { get; }
		}

		public class MethodTable { }

		/// <summary>
		///     This is the base class for declaring states and their inheritance relationships.
		///     Don't create instances of this class, instead set the state using SetState.
		/// </summary>
		/// <remarks>
		///     This class doesn't actually *do* anything (except for providing a place to store the
		///     method table). It's mostly just a convenient way to express inheritance relationships
		///     between states using an existing C# mechanism. You can then, for example, use the "is"
		///     operator on the current state.
		/// </remarks>
		public class State // <- NOTE: Not abstract, because this is the default state for when no other state is set
		{
			// Serializer probably should not walk into this field
			public MethodTable methodTable = null!;
		}

		#region Lookup Helpers

		// Hope that the .NET type lookup here on TState is fastest option (it should be).
		// Would love to look up on the derived type as well, but it is dynamic.
		private static class StateInstanceLookup<TState> where TState : State, new()
		{
			// For any given state, a more-derived class might provide an instance of that state
			internal static readonly Dictionary<Type, TState> forType = new();

			// This method exists so we don't have to mess with reflection on generics to access forType
			internal static void Add(Type stateMachineType, TState state)
			{
				forType.Add(stateMachineType, state);
			}

			// This method exists so we don't have to mess with reflection on generics to access forType
			internal static TState? Get(Type stateMachineType)
			{
                forType.TryGetValue(stateMachineType, out var result);
				return result;
			}
		}

		public TState GetState<TState>() where TState : State, new()
		{
			// We need to get our own true type - that is, the fully-derived type, so that we get the correct method table
			//
			// For example: If we are an instance of class B, which inherits from class A, and a method in class A is called
			// which sets state Q, we want to get B's method table for state Q.
			var type = GetType();

			return StateInstanceLookup<TState>.forType[type];
		}


		public static TState GetState<TType, TState>()
			where TType : StateProvider
			where TState : State, new()
		{
			return StateInstanceLookup<TState>.forType[typeof(TType)];
		}

		public static ReadOnlyList<State> GetAllStatesFor(Type type)
		{
			return new ReadOnlyList<State>(allStatesByType![type]);
		}
		
		public State? GetStateBySymbol(string symbol)
		{
			// We need to get our own true type - that is, the fully-derived type, so that we get the correct method table
			//
			// For example: If we are an instance of class B, which inherits from class A, and a method in class A is called
			// which sets state Q, we want to get B's method table for state Q.
			var type = GetType();

			foreach (var state in allStatesByType![type])
			{
				var queryable = state as IHaveSymbol;
				if (queryable == null)
					continue;

				if (queryable.Symbol == symbol)
					return state;
			}

			return null;
		}

		public static List<State>? DeveloperGetAllStatesByType(Type type)
		{
			return allStatesByType?[type];
		}

		#endregion

		#region Setup

		/// <summary>Initialize all state machines. Can only be called once.</summary>
		public static void Setup(Assembly[] assemblies)
		{
			if (Interlocked.CompareExchange(ref allStateInstances, new List<State>(), null) != null)
			{
				Debug.Assert(false); // <- Programmer threading fail
				throw new InvalidOperationException("StateProvider was already setup");
			}
			// At this point, we own AllStateInstances

			var stateMachinesToStates = new Dictionary<Type, Dictionary<Type, State>>();
			var stateMachinesToAbstractStates = new Dictionary<Type, Dictionary<Type, MethodTable>>();

			// Create all states for all state machines:
			var allStateMachineTypes = assemblies.SelectMany(a => a.GetTypes())
				.Where(t => typeof(StateProvider).IsAssignableFrom(t));

			foreach (var type in allStateMachineTypes)
				SetupStateMachineTypeRecursive(stateMachinesToStates, stateMachinesToAbstractStates, type);

			// Unpack generated information into the appropriate lookups
			// IMPORTANT: The AllStateInstances lookup gets used by networking - must be network-safe-ordered!!
			allStatesByType = new Dictionary<Type, List<State>>();
			foreach (var stateMachineAndStates in stateMachinesToStates.NetworkOrder(kvp => kvp.Key.ToString()))
			foreach (var state in stateMachineAndStates.Value.NetworkOrder(kvp => kvp.Key.ToString()))
			{
				AllStateInstances.Add(state.Value);

				// TODO: This smells weird (unnecessary lookup by key we already have??)
                if (!allStatesByType.TryGetValue(stateMachineAndStates.Key, out var states))
				{
					states = new List<State>();
					allStatesByType.Add(stateMachineAndStates.Key, states);
				}

				allStatesByType[stateMachineAndStates.Key].Add(state.Value);

				var stateInstanceLookupType = typeof(StateInstanceLookup<>).MakeGenericType(state.Key);
				stateInstanceLookupType
					.GetMethod("Add", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?.Invoke(null, new object[] {stateMachineAndStates.Key, state.Value});
			}
		}


		private static string GetStateName(MemberInfo stateType)
		{
			var stateName = stateType.Name;
			if (stateName != "State" && stateName.EndsWith("State"))
				stateName = stateName.Substring(0, stateName.Length - "State".Length);
			return stateName;
		}


		private static void SetupStateMachineTypeRecursive(
			Dictionary<Type, Dictionary<Type, State>> stateMachinesToStates,
			Dictionary<Type, Dictionary<Type, MethodTable>> stateMachinesToAbstractStates,
			Type stateMachineType)
		{
			Debug.Assert(typeof(StateProvider).IsAssignableFrom(stateMachineType));

			if (stateMachinesToStates.ContainsKey(stateMachineType))
				return; // We've already visited this type

			// Recursively process all ancestors, then fetch base type's states
			Dictionary<Type, State> baseStates = null!;
			Dictionary<Type, MethodTable> baseAbstractStates = null!;
			if (stateMachineType != typeof(StateProvider))
			{
				SetupStateMachineTypeRecursive(stateMachinesToStates, stateMachinesToAbstractStates, stateMachineType.BaseType!);
				baseStates = stateMachinesToStates[stateMachineType.BaseType!];
				baseAbstractStates = stateMachinesToAbstractStates[stateMachineType.BaseType!];
			}


			// "Used" state methods will be removed from this list, because we want to report errors if any are unused (indicates likely end-user error)
			var stateMethods = stateMachineType
				.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
				            BindingFlags.DeclaredOnly)
				.Where(mi => mi.Name.StartsWith("State_")).ToDictionary(mi => mi.Name);


			// Get our method table type:
			Type methodTableType;
			var methodTableSearchType = stateMachineType;
			while ((methodTableType = methodTableSearchType.GetNestedType("MethodTable", BindingFlags.Public | BindingFlags.NonPublic)!) == null)
			{
				if (!typeof(StateProvider).IsAssignableFrom(methodTableSearchType.BaseType))
					break;
				methodTableSearchType = methodTableSearchType.BaseType;
			}

			if (methodTableType == null)
				throw new InvalidOperationException("MethodTable not found for " + stateMachineType);

			if (!typeof(MethodTable).IsAssignableFrom(methodTableType))
				throw new InvalidOperationException("MethodTable must be derived from StateMachine.MethodTable");

			// NOTE: There is more correctness checking we could be doing for MethodTable, but it is complicated because
			//       our parent type may be using a grandparent's MethodTable, so the check would have to be recursive.
			//       Instead we just get the type from the parent's State's MethodTable and assume it's correct.
			
			var states = new Dictionary<Type, State>();
			var abstractStates = new Dictionary<Type, MethodTable>();

			// Created derived versions of all of our base type's states
			Debug.Assert(baseStates != null == (baseAbstractStates != null));
			if (baseStates != null)
			{
				// This is *not* recursive, because in derived state machines, we want overriding
				// methods to only override the specified state's method - not *child* states 
				// that are specified by the (grand-)parent state machine(s). If we want to follow
				// the state inheritance hierarchy instead, we can do that manually.

				foreach (var baseState in baseStates)
				{
					// The state type remains the same, but the method table gets derived
					var state = (State) Activator.CreateInstance(baseState.Key)!;
					state.methodTable = ShallowCloneToDerived(baseState.Value.methodTable, methodTableType, stateMachineType);
					FillMethodTableWithOverrides(baseState.Key, state.methodTable, stateMachineType, stateMethods);

					states.Add(baseState.Key, state);
				}

				foreach (var baseAbstractState in baseAbstractStates!)
				{
					// The state type remains the same, but the method table gets derived
					var methodTable = ShallowCloneToDerived(baseAbstractState.Value, methodTableType, stateMachineType);
					FillMethodTableWithOverrides(baseAbstractState.Key, methodTable, stateMachineType, stateMethods);

					abstractStates.Add(baseAbstractState.Key, methodTable);
				}
			}

			// Create state instances for all states declared by the current state machine (recursive to handle state inheritance)
            var newStateTypes = stateMachineType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(nt => typeof(State).IsAssignableFrom(nt)).ToArray();

			foreach (var stateType in newStateTypes)
				SetupStateTypeRecursive(states, abstractStates, stateType, stateMachineType, methodTableType,
					stateMethods);

            if (stateMethods.Count > 0)
			{
				Debug.Assert(false);
				throw new Exception($"State methods were unused (probably a naming error or undefined state):\n{string.Join("\n", stateMethods.Values)}");
			}


			// Fill in any delegates that are still null with empty methods
			// This is so calling does not require a null check (probably slower due to jump, but makes coding far easier!)
			var stateTypesToMethodTables = states
				.Select(kvp => new KeyValuePair<Type, MethodTable>(kvp.Key, kvp.Value.methodTable))
				.Concat(abstractStates);

			foreach (var (stateType, methodTable) in stateTypesToMethodTables)
			{
                var allMethodTableEntries = methodTable.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
					.Where(fi => fi.FieldType.BaseType == typeof(MulticastDelegate));

				foreach (var fieldInfo in allMethodTableEntries)
				{
					if (fieldInfo.GetCustomAttributes(typeof(AlwaysNullCheckedAttribute), true).Length != 0)
						continue; // Don't need a default implementation if we promise to always null-check

					if (fieldInfo.GetValue(methodTable) == null)
					{
						var methodInMethodTable = fieldInfo.FieldType.GetMethod("Invoke");

						var dynamicMethod = new DynamicMethod(
                            $"DoNothing_{GetStateName(stateType)}_{fieldInfo.Name}",
							methodInMethodTable!.ReturnType,
							methodInMethodTable.GetParameters().Select(pi => pi.ParameterType).ToArray(),
							stateMachineType);

						var il = dynamicMethod.GetILGenerator();
						EmitDefault(il, methodInMethodTable.ReturnType);
						il.Emit(OpCodes.Ret);

						fieldInfo.SetValue(methodTable, dynamicMethod.CreateDelegate(fieldInfo.FieldType));
					}
				}
			}
			
			stateMachinesToStates.Add(stateMachineType, states);
			stateMachinesToAbstractStates.Add(stateMachineType, abstractStates);
		}

		private static void EmitDefault(ILGenerator il, Type type)
		{
			if (type == typeof(void))
				return; // No default to emit

			switch (Type.GetTypeCode(type))
			{
				case TypeCode.Boolean:
				case TypeCode.Char:
				case TypeCode.SByte:
				case TypeCode.Byte:
				case TypeCode.Int16:
				case TypeCode.UInt16:
				case TypeCode.Int32:
				case TypeCode.UInt32:
					il.Emit(OpCodes.Ldc_I4_0);
					break;

				case TypeCode.Int64:
				case TypeCode.UInt64:
					il.Emit(OpCodes.Ldc_I4_0);
					il.Emit(OpCodes.Conv_I8);
					break;

				case TypeCode.Single:
					il.Emit(OpCodes.Ldc_R4, default(float));
					break;

				case TypeCode.Double:
					il.Emit(OpCodes.Ldc_R8, default(double));
					break;

                case TypeCode.Empty:
                case TypeCode.Object:
                case TypeCode.DBNull:
                case TypeCode.Decimal:
                case TypeCode.DateTime:
                case TypeCode.String:
                default:
					if (type.IsValueType)
					{
						var lb = il.DeclareLocal(type);
						il.Emit(OpCodes.Ldloca, lb);
						il.Emit(OpCodes.Initobj, type);
						il.Emit(OpCodes.Ldloc, lb);
					}
					else
					{
						il.Emit(OpCodes.Ldnull);
					}

					break;
			}
		}


		private static void SetupStateTypeRecursive(IDictionary<Type, State> states, IDictionary<Type, MethodTable> abstractStates, Type stateType, Type stateMachineType, Type methodTableType, Dictionary<string, MethodInfo> stateMethods)
		{
			if (states.ContainsKey(stateType) || abstractStates.ContainsKey(stateType))
				return; // Already processed

			if (stateType == typeof(State) && stateMachineType == typeof(StateProvider))
			{
				Debug.Assert(!stateType.IsAbstract); // <- NOTE: Following needs to add to the correct table...
				states.Add(stateType,
					new State {methodTable = new MethodTable()}); // No parent, so we must create directly
				return;
			}

			// State type 'State' should either have been setup above as the root state (in StateMachine)
			// or found as already existing in 'states' (handled by inheritance handling outside this method)
			Debug.Assert(stateType != typeof(State));

            // Recurse to parents
			SetupStateTypeRecursive(states, abstractStates, stateType.BaseType!, stateMachineType, methodTableType, stateMethods);

            // Create by copying in values from our parent state, then overriding
            var parentMethodTable = stateType.BaseType!.IsAbstract
                ? abstractStates[stateType.BaseType]
                : states[stateType.BaseType].methodTable;

			var methodTable = ShallowCloneToDerived(parentMethodTable, methodTableType, stateMachineType);
			FillMethodTableWithOverrides(stateType, methodTable, stateMachineType, stateMethods);
			
			// Output
			if (stateType.IsAbstract)
			{
				abstractStates.Add(stateType, methodTable);
			}
			else
			{
				var state = (State) Activator.CreateInstance(stateType)!;
				state.methodTable = methodTable;
				states.Add(stateType, state);
			}
		}


		private static MethodTable ShallowCloneToDerived(MethodTable state, Type derivedType, Type stateMachineType)
		{
			var baseType = state.GetType();
			if (!baseType.IsAssignableFrom(derivedType))
				throw new Exception("Method table inheritance hierarchy error.");

			if (derivedType.IsGenericType)
				derivedType = derivedType.MakeGenericType(stateMachineType.GetGenericArguments()[0]);

			var derivedMethodTable = (MethodTable) Activator.CreateInstance(derivedType)!;

			// Copy all fields that exist in the base type to the more derived type
			foreach (var field in baseType.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
			                                         BindingFlags.Instance))
				field.SetValue(derivedMethodTable, field.GetValue(state));

			return derivedMethodTable;
		}


        /// <summary>Find methods from the state machine type to insert into the method table</summary>
        /// <param name="stateMachineType"></param>
        /// <param name="stateMethods">Methods from the state machine type. "Used" methods will be removed.</param>
        /// <param name="stateType"></param>
        /// <param name="methodTable"></param>
        private static void FillMethodTableWithOverrides(Type stateType, MethodTable methodTable, Type stateMachineType, IDictionary<string, MethodInfo> stateMethods)
		{
			var allMethodTableEntries = methodTable.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
				.Where(fi => fi.FieldType.BaseType == typeof(MulticastDelegate));

			foreach (var fieldInfo in allMethodTableEntries)
			{
				var potentialMethodName = $"State_{GetStateName(stateType)}_{fieldInfo.Name}";

                if (!stateMethods.TryGetValue(potentialMethodName, out var methodInStateMachine))
                    continue;

                var methodInMethodTable = fieldInfo.FieldType.GetMethod("Invoke");

                // Check the method signatures match...
                if (methodInStateMachine.ReturnType != methodInMethodTable!.ReturnType)
                    ThrowMethodMismatch(methodInStateMachine, methodInMethodTable);

                var methodInMethodTableParameters = methodInMethodTable.GetParameters();
                var methodInStateMachineParameters = methodInStateMachine.GetParameters();

                if (methodInStateMachineParameters.Length != methodInMethodTableParameters.Length - 1
                   ) // -1 to account for 'this' parameter to open delegate
                    ThrowMethodMismatch(methodInStateMachine, methodInMethodTable);

                for (var i = 0; i < methodInStateMachineParameters.Length; i++)
                    if (methodInStateMachineParameters[i].ParameterType !=
                        methodInMethodTableParameters[i + 1]
                            .ParameterType && // +1 to account for 'this' parameter to open delegate     
                        !methodInMethodTableParameters[i + 1].ParameterType
                            .IsAssignableFrom(methodInStateMachineParameters[i].ParameterType)
                       ) // i.e. supports custom implementations of IUpdateContext
                        ThrowMethodMismatch(methodInStateMachine, methodInMethodTable);
					
                // Check whether we need a down-casting shim (because the method was
                // declared for a base type of the current stateMachineType)
                if (!stateMachineType.IsAssignableFrom(methodInMethodTableParameters[0].ParameterType))
                {
                    Debug.Assert(methodInMethodTableParameters[0].ParameterType
                        .IsAssignableFrom(stateMachineType)); // (other direction)

                    var dynamicMethod = new DynamicMethod($"CastingShim_{potentialMethodName}",
                        methodInMethodTable.ReturnType,
                        methodInMethodTableParameters.Select(pi => pi.ParameterType).ToArray(), stateMachineType);

                    var il = dynamicMethod.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, stateMachineType); // <- the casting bit of the shim
                    if (methodInMethodTableParameters.Length > 1) il.Emit(OpCodes.Ldarg_1);
                    if (methodInMethodTableParameters.Length > 2) il.Emit(OpCodes.Ldarg_2);
                    if (methodInMethodTableParameters.Length > 3) il.Emit(OpCodes.Ldarg_3);
                    for (var i = 4; i < methodInMethodTableParameters.Length; i++)
                        if (i <= byte.MaxValue)
                            il.Emit(OpCodes.Ldarg_S, (byte) i);
                        else
                            il.Emit(OpCodes.Ldarg, (ushort) i);
                    il.Emit(OpCodes.Callvirt, methodInStateMachine); // <- the call forwarding bit of the shim
                    il.Emit(OpCodes.Ret);

                    fieldInfo.SetValue(methodTable, dynamicMethod.CreateDelegate(fieldInfo.FieldType));
                }
                else
                {
                    // No shim required, create an open delegate
                    fieldInfo.SetValue(methodTable,
                        Delegate.CreateDelegate(fieldInfo.FieldType, methodInStateMachine));
                }

                stateMethods.Remove(potentialMethodName);
            }
		}

        private static void ThrowMethodMismatch(MethodInfo methodInStateMachine, MethodInfo methodInMethodTable) => throw new Exception($"Method signature does not match: \"{methodInStateMachine}\" cannot be used for \"{methodInMethodTable}\"");

        #endregion
	}
}