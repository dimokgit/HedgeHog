// hardcodet.net Lambda Dependencies
// Copyright (c) 2009 Philipp Sumi
// Contact and Information: http://www.hardcodet.net
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the Code Project Open License (CPOL);
// either version 1.0 of the License, or (at your option) any later
// version.
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
//
// THIS COPYRIGHT NOTICE MAY NOT BE REMOVED FROM THIS FILE.


using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Hardcodet.Util.Dependencies
{
  /// <summary>
  /// Encapsulates a 
  /// </summary>
  /// <typeparam name="TSource"></typeparam>
  /// <typeparam name="TTarget"></typeparam>
  public sealed class LambdaBinding<TSource, TTarget> : LambdaBinding
  {
    #region properties

    /// <summary>
    /// Observes the object graph that makes the binding source.
    /// </summary>
    public DependencyNode<TSource> SourceDependency { get; private set; }

    /// <summary>
    /// Observes the object graph that makes the binding target.
    /// </summary>
    public DependencyNode<TTarget> TargetDependency { get; private set; }

    /// <summary>
    /// The default value that is being assigned to the
    /// binding target if the source dependency is being
    /// broken. If not set, the the default value (e.g. null)
    /// of the target type is being assigned to the binding
    /// target.
    /// </summary>
    public TTarget DefaultValue { get; set; }

    /// <summary>
    /// An optional converter that is used in one-way or two-way binding
    /// scenarios to convert a source value into a value that is being
    /// assigned to the binding target.
    /// </summary>
    public Func<TSource, TTarget> ForwardConverter { get; internal set; }

    /// <summary>
    /// An optional converter that is used in two-way binding scenarios
    /// to convert a target value into a value that is being assigned
    /// to the binding source.
    /// </summary>
    public Func<TTarget, TSource> ReverseConverter { get; internal set; }

    /// <summary>
    /// Points to the targeted member.
    /// </summary>
    public MemberInfo TargetMember { get; internal set; }

    /// <summary>
    /// Points to the member that provides the source value.
    /// </summary>
    public MemberInfo SourceMember { get; internal set; }

    /// <summary>
    /// Indicates whether updates flow only from source to target
    /// or in both directions.
    /// </summary>
    public bool IsTwoWayBinding { get; set; }

    #endregion

    #region construction

    /// <summary>
    /// 
    /// </summary>
    /// <param name="source"></param>
    /// <param name="target"></param>
    /// <param name="isTwoWayBinding"></param>
    internal LambdaBinding(Expression<Func<TSource>> source, Expression<Func<TTarget>> target, bool isTwoWayBinding)
    {
      IsTwoWayBinding = isTwoWayBinding;

      //create dependency on source
      SourceDependency = DependencyNode.Create(source);
      SourceDependency.DependencyChanged += OnSourceChanged;
      SourceMember = SourceDependency.LeafNode.ParentMember;

      //in case of a two way binding, create dependency on target
      TargetDependency = DependencyNode.Create(target);
      TargetMember = TargetDependency.LeafNode.ParentMember;

      //only observe the target node in two-way binding scenarios
      if (isTwoWayBinding)
      {
        TargetDependency.DependencyChanged += OnTargetChanged;
      }
    }

    #endregion

    #region source / target node change events

    /// <summary>
    /// Event listener that is being invoked in one-way and two-way binding
    /// scenarios whenever the source node value is being changed.
    /// </summary>
    private void OnSourceChanged(object sender, DependencyChangeEventArgs<TSource> e)
    {
      //if we don't have a valid chain, skip immediately
      if (TargetDependency.RootNode.IsChainBroken) return;

      TTarget targetValue;
      if (e.ChangedNode.IsChainBroken)
      {
        targetValue = DefaultValue;
      }
      else
      {
        TSource sourceValue = e.TryGetLeafValue();
        if (ForwardConverter != null)
        {
          targetValue = ForwardConverter(sourceValue);
        }
        else
        {
          targetValue = (TTarget) (object) sourceValue;
        }
      }

      //assign to target
      var parent = TargetDependency.LeafNode.ParentNode.NodeValue;
      SetMemberValue(TargetMember, parent, targetValue);
    }


    /// <summary>
    /// Event listener that is being invoked in two-way binding scenarios whenever
    /// the target node value is being changed.
    /// </summary>
    private void OnTargetChanged(object sender, DependencyChangeEventArgs<TTarget> e)
    {
      //if the target is broken or the source is not accessible, do not update the source
      if (SourceDependency.RootNode.IsChainBroken || TargetDependency.RootNode.IsChainBroken) return;

      TTarget targetValue = e.TryGetLeafValue();

      TSource sourceValue;
      if (ReverseConverter != null)
      {
        sourceValue = ReverseConverter(targetValue);
      }
      else
      {
        sourceValue = (TSource) (object) targetValue;
      }

      //only update the source if its value is different than the
      //current one - re-assiging the same value produces a recursive
      //update
      if (sourceValue.Equals(SourceDependency.LeafValue)) return;

      //assign to source
      var parent = SourceDependency.LeafNode.ParentNode.NodeValue;
      SetMemberValue(SourceMember, parent, sourceValue);
    }

    #endregion

    #region SetMemberValue

    /// <summary>
    /// Updates an object's given member with a specified value.
    /// </summary>
    /// <param name="memberInfo">Provides information about the member
    /// (field or property) that needs to be updated.</param>
    /// <param name="target">The object that provides the member that is
    /// being updated.</param>
    /// <param name="memberValue">The value to be assigned to the field or
    /// property that is being updated.</param>
    /// <exception cref="InvalidOperationException">If the <paramref name="memberInfo"/>
    /// if neither a <see cref="PropertyInfo"/> nor a <see cref="FieldInfo"/>.</exception>
    private static void SetMemberValue(MemberInfo memberInfo, object target, object memberValue)
    {
      var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

      switch (memberInfo.MemberType)
      {
        case MemberTypes.Field:
          var fi = (FieldInfo) memberInfo;
          fi.SetValue(target, memberValue);
          break;
        case MemberTypes.Property:
          var pi = (PropertyInfo) memberInfo;
          pi.SetValue(target, memberValue, flags, null, null, null);
          break;
        default:
          string msg = "Unsupported child member type: {0}";
          msg = String.Format(msg, memberInfo.MemberType);
          throw new InvalidOperationException(msg);
      }
    }

    #endregion

    #region Dispose

    /// <summary>
    /// Disposes the underlying bindings.
    /// </summary>
    public override void Dispose()
    {
      //deregister event listeners
      SourceDependency.DependencyChanged -= OnSourceChanged;
      if (IsTwoWayBinding) TargetDependency.DependencyChanged -= OnTargetChanged;

      //clear dependencies and references
      SourceDependency.Dispose();
      TargetDependency.Dispose();
      SourceDependency = null;
      TargetDependency = null;
    }

    #endregion
  }


  /// <summary>
  /// A binding class that links two object graphs using
  /// lambda dependencies.
  /// </summary>
  public abstract class LambdaBinding : IDisposable
  {
    /// <summary>
    /// Internal constructor effectively seals the
    /// class for outside classes.
    /// </summary>
    internal LambdaBinding()
    {
    }


    /// <summary>
    /// Creates a simple one-way binding that does not require
    /// type conversion.
    /// </summary>
    /// <typeparam name="T">The type of both source and target value.</typeparam>
    /// <param name="source">An expression that points to the source that is
    /// being observed.</param>
    /// <param name="target">The target node that is being updated if possible.</param>
    /// <returns>A binding object that manages the dependency on both <paramref name="source"/>
    /// and <paramref name="target"/> items.</returns>
    public static LambdaBinding BindOneWay<T>(Expression<Func<T>> source,
                                              Expression<Func<T>> target)
    {
      return BindOneWay(source, target, default(T));
    }


    /// <summary>
    /// Creates a simple one-way binding that does not require
    /// type conversion.
    /// </summary>
    /// <typeparam name="T">The type of both source and target value.</typeparam>
    /// <param name="source">An expression that points to the source that is
    /// being observed.</param>
    /// <param name="target">The target node that is being updated if possible.</param>
    /// <param name="defaultValue">The default value to be assigned to the target if
    /// the source binding is broken.</param>
    /// <returns>A binding object that manages the dependency on both <paramref name="source"/>
    /// and <paramref name="target"/> items.</returns>
    public static LambdaBinding BindOneWay<T>(Expression<Func<T>> source,
                                              Expression<Func<T>> target,
                                              T defaultValue)
    {
      return BindOneWay(source, target, null, defaultValue);
    }


    /// <summary>
    /// Creates a simple one-way binding that performs an optional data conversion
    /// from source to target.
    /// </summary>
    /// <typeparam name="TSource">The type of the source value.</typeparam>
    /// <typeparam name="TTarget">The type of the target value.</typeparam>
    /// <param name="source">An expression that points to the source that is
    /// being observed.</param>
    /// <param name="target">The target node that is being updated if possible.</param>
    /// <param name="converter">An optional converter that performs type conversion from
    /// <typeparamref name="TSource"/> to <typeparamref name="TTarget"/>. In case this
    /// parameter is null, the binding tries to perform an implicit cast.</param>
    /// <returns>A binding object that manages the dependency on both <paramref name="source"/>
    /// and <paramref name="target"/> items.</returns>
    public static LambdaBinding BindOneWay<TSource, TTarget>(
        Expression<Func<TSource>> source, Expression<Func<TTarget>> target, Func<TSource, TTarget> converter)
    {
      return BindOneWay(source, target, converter, default(TTarget));
    }


    /// <summary>
    /// Creates a simple one-way binding that performs an optional data conversion
    /// from source to target.
    /// </summary>
    /// <typeparam name="TSource">The type of the source value.</typeparam>
    /// <typeparam name="TTarget">The type of the target value.</typeparam>
    /// <param name="source">An expression that points to the source that is
    /// being observed.</param>
    /// <param name="target">The target node that is being updated if possible.</param>
    /// <param name="converter">An optional converter that performs type conversion from
    /// <typeparamref name="TSource"/> to <typeparamref name="TTarget"/>. In case this
    /// parameter is null, the binding tries to perform an implicit cast.</param>
    /// <param name="defaultValue">The default value to be assigned to the target if
    /// the source binding is broken.</param>
    /// <returns>A binding object that manages the dependency on both <paramref name="source"/>
    /// and <paramref name="target"/> items.</returns>
    public static LambdaBinding BindOneWay<TSource, TTarget>(
        Expression<Func<TSource>> source, Expression<Func<TTarget>> target, Func<TSource, TTarget> converter,
        TTarget defaultValue)
    {
      return Bind(source, target, converter, null, defaultValue, false);
    }


    /// <summary>
    /// Creates a simple two-way binding that does not require type
    /// conversions.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source">An expression that points to the source that is
    /// being observed.</param>
    /// <param name="target">The target node that is being updated if possible.</param>
    /// <returns>A binding object that manages the dependency on both <paramref name="source"/>
    /// and <paramref name="target"/> items.</returns>
    public static LambdaBinding BindTwoWay<T>(
        Expression<Func<T>> source, Expression<Func<T>> target)
    {
      return BindTwoWay(source, target, default(T));
    }


    /// <summary>
    /// Creates a simple two-way binding that does not require type
    /// conversions.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source">An expression that points to the source that is
    /// being observed.</param>
    /// <param name="target">The target node that is being updated if possible.</param>
    /// <param name="defaultValue">The default value to be assigned to the target if
    /// the source binding is broken.</param>
    /// <returns>A binding object that manages the dependency on both <paramref name="source"/>
    /// and <paramref name="target"/> items.</returns>
    public static LambdaBinding BindTwoWay<T>(
        Expression<Func<T>> source, Expression<Func<T>> target, T defaultValue)
    {
      return BindTwoWay(source, target, null, null, defaultValue);
    }


    /// <summary>
    /// Creates a simple two-way binding between two objects.
    /// </summary>
    /// <typeparam name="TSource"></typeparam>
    /// <typeparam name="TTarget"></typeparam>
    /// <param name="source">An expression that points to the source that is
    /// being observed.</param>
    /// <param name="target">The target node that is being updated if
    /// possible.</param>
    /// <param name="forwardConverter">An optional converter that performs
    /// type conversion from <typeparamref name="TSource"/> to
    /// <typeparamref name="TTarget"/> if the source was updated.
    /// In case this parameter is null, the binding tries to perform
    /// an implicit cast.</param>
    /// <param name="reverseConverter">An optional converter that performs
    /// type conversion from <typeparamref name="TTarget"/> to
    /// <typeparamref name="TSource"/> if the target was updated.
    /// In case this parameter is null, the binding tries to perform
    /// an implicit cast.</param>
    /// <returns>A binding object that manages the dependency on both 
    /// <paramref name="source"/> and <paramref name="target"/> items.</returns>
    public static LambdaBinding BindTwoWay<TSource, TTarget>(
        Expression<Func<TSource>> source, Expression<Func<TTarget>> target, Func<TSource, TTarget> forwardConverter,
        Func<TTarget, TSource> reverseConverter)
    {
      return BindTwoWay(source, target, forwardConverter, reverseConverter, default(TTarget));
    }


    /// <summary>
    /// Creates a simple two-way binding between two objects.
    /// </summary>
    /// <typeparam name="TSource"></typeparam>
    /// <typeparam name="TTarget"></typeparam>
    /// <param name="source">An expression that points to the source that is
    /// being observed.</param>
    /// <param name="target">The target node that is being updated if
    /// possible.</param>
    /// <param name="forwardConverter">An optional converter that performs
    /// type conversion from <typeparamref name="TSource"/> to
    /// <typeparamref name="TTarget"/> if the source was updated.
    /// In case this parameter is null, the binding tries to perform
    /// an implicit cast.</param>
    /// <param name="reverseConverter">An optional converter that performs
    /// type conversion from <typeparamref name="TTarget"/> to
    /// <typeparamref name="TSource"/> if the target was updated.
    /// In case this parameter is null, the binding tries to perform
    /// an implicit cast.</param>
    /// <param name="defaultValue">The default value to be assigned to the target if
    /// the source binding is broken.</param>
    /// <returns>A binding object that manages the dependency on both 
    /// <paramref name="source"/> and <paramref name="target"/> items.</returns>
    public static LambdaBinding BindTwoWay<TSource, TTarget>(
        Expression<Func<TSource>> source, Expression<Func<TTarget>> target, Func<TSource, TTarget> forwardConverter,
        Func<TTarget, TSource> reverseConverter, TTarget defaultValue)
    {
      return Bind(source, target, forwardConverter, reverseConverter, defaultValue, true);
    }



    /// <summary>
    /// Creates the actual binding.
    /// </summary>
    private static LambdaBinding Bind<TSource, TTarget>(
        Expression<Func<TSource>> source, Expression<Func<TTarget>> target, Func<TSource, TTarget> forwardConverter,
        Func<TTarget, TSource> reverseConverter, TTarget defaultValue, bool isTwoWayBinding)
    {
      var binding = new LambdaBinding<TSource, TTarget>(source, target, isTwoWayBinding);
      binding.ForwardConverter = forwardConverter;
      binding.ReverseConverter = reverseConverter;
      binding.DefaultValue = defaultValue;
      return binding;
    }


    /// <summary>
    /// Disposes the binding.
    /// </summary>
    public abstract void Dispose();
  }
}