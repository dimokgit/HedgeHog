// Copyright (c) 2008 Daniel Grunwald
// 
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Hardcodet.Util.Dependencies.WeakEvents
{
  /// <summary>
  /// A class for managing a weak event.
  /// </summary>
  [DebuggerNonUserCode]
  public sealed class WeakEvent<T> where T : class
  {
    [DebuggerNonUserCode]
    struct EventEntry
    {
      public readonly FastSmartWeakEventForwarderProvider.ForwarderDelegate Forwarder;
      public readonly MethodInfo TargetMethod;
      public readonly WeakReference TargetReference;
			
      public EventEntry(FastSmartWeakEventForwarderProvider.ForwarderDelegate Forwarder, MethodInfo targetMethod, WeakReference targetReference)
      {
        this.Forwarder = Forwarder;
        this.TargetMethod = targetMethod;
        this.TargetReference = targetReference;
      }
    }
		
    readonly List<EventEntry> eventEntries = new List<EventEntry>();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations")]
    static WeakEvent()
    {
      if (!typeof(T).IsSubclassOf(typeof(Delegate)))
        throw new ArgumentException("T must be a delegate type");
      MethodInfo invoke = typeof(T).GetMethod("Invoke");
      if (invoke == null || invoke.GetParameters().Length != 2)
        throw new ArgumentException("T must be a delegate type taking 2 parameters");
      ParameterInfo senderParameter = invoke.GetParameters()[0];
      if (senderParameter.ParameterType != typeof(object))
        throw new ArgumentException("The first delegate parameter must be of type 'object'");
      ParameterInfo argsParameter = invoke.GetParameters()[1];
      if (!argsParameter.ParameterType.IsSubclassOf(typeof(EventArgs)))
        throw new ArgumentException("The second delegate parameter must be derived from type 'EventArgs'. Type is " + argsParameter.ParameterType);
      if (invoke.ReturnType != typeof(void))
        throw new ArgumentException("The delegate return type must be void.");
    }
		
    public void Add(T eh)
    {
      if (eh != null) {
        Delegate d = (Delegate)(object)eh;
        if (eventEntries.Count == eventEntries.Capacity)
          RemoveDeadEntries();
        WeakReference target = d.Target != null ? new WeakReference(d.Target) : null;
        eventEntries.Add(new EventEntry(FastSmartWeakEventForwarderProvider.GetForwarder(d.Method), d.Method, target));
      }
    }
		
    void RemoveDeadEntries()
    {
      eventEntries.RemoveAll(ee => ee.TargetReference != null && !ee.TargetReference.IsAlive);
    }


    public void Clear()
    {
      eventEntries.Clear();
    }
		
    public void Remove(T eh)
    {
      if (eh != null) {
        Delegate d = (Delegate)(object)eh;
        for (int i = eventEntries.Count - 1; i >= 0; i--) {
          EventEntry entry = eventEntries[i];
          if (entry.TargetReference != null) {
            object target = entry.TargetReference.Target;
            if (target == null) {
              eventEntries.RemoveAt(i);
            } else if (target == d.Target && entry.TargetMethod == d.Method) {
              eventEntries.RemoveAt(i);
              break;
            }
          } else {
            if (d.Target == null && entry.TargetMethod == d.Method) {
              eventEntries.RemoveAt(i);
              break;
            }
          }
        }
      }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2109:ReviewVisibleEventHandlers")]
    public void Raise(object sender, EventArgs e)
    {
      bool needsCleanup = false;
      foreach (EventEntry ee in eventEntries.ToArray()) {
        needsCleanup |= ee.Forwarder(ee.TargetReference, sender, e);
      }
      if (needsCleanup)
        RemoveDeadEntries();
    }
  }

  [DebuggerNonUserCode]
  static class FastSmartWeakEventForwarderProvider
  {
    static readonly MethodInfo getTarget = typeof(WeakReference).GetMethod("get_Target");
    static readonly Type[] forwarderParameters = { typeof(WeakReference), typeof(object), typeof(EventArgs) };
    internal delegate bool ForwarderDelegate(WeakReference wr, object sender, EventArgs e);
		
    static readonly Dictionary<MethodInfo, ForwarderDelegate> forwarders = new Dictionary<MethodInfo, ForwarderDelegate>();
		
    internal static ForwarderDelegate GetForwarder(MethodInfo method)
    {
      lock (forwarders) {
        ForwarderDelegate d;
        if (forwarders.TryGetValue(method, out d))
          return d;
      }
			
      if (method.DeclaringType.GetCustomAttributes(typeof(CompilerGeneratedAttribute), false).Length != 0)
        throw new ArgumentException("Cannot create weak event to anonymous method with closure.");
			
      Debug.Assert(getTarget != null);
			
      DynamicMethod dm = new DynamicMethod(
          "FastSmartWeakEvent", typeof(bool), forwarderParameters, method.DeclaringType);
			
      ILGenerator il = dm.GetILGenerator();
			
      if (!method.IsStatic) {
        il.Emit(OpCodes.Ldarg_0);
        il.EmitCall(OpCodes.Callvirt, getTarget, null);
        il.Emit(OpCodes.Dup);
        Label label = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, label);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(label);
      }
      il.Emit(OpCodes.Ldarg_1);
      il.Emit(OpCodes.Ldarg_2);
      il.EmitCall(OpCodes.Call, method, null);
      il.Emit(OpCodes.Ldc_I4_0);
      il.Emit(OpCodes.Ret);
			
      ForwarderDelegate fd = (ForwarderDelegate)dm.CreateDelegate(typeof(ForwarderDelegate));
      lock (forwarders) {
        forwarders[method] = fd;
      }
      return fd;
    }
  }
}