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


namespace Hardcodet.Util.Dependencies
{
  /// <summary>
  /// Creates and maintains <see cref="DependencyNode{T}"/>
  /// instances.
  /// </summary>
  public class DependencyBuilder
  {
    /// <summary>
    /// Creates dependency chain of <see cref="DependencyNode{T}"/> instances for a given
    /// expression.
    /// </summary>
    /// <typeparam name="T">The type of the expression's compiled return value.</typeparam>
    /// <param name="target">The expression that declares the dependency chain from root
    /// to the target item or value.</param>
    /// <returns>The root dependency node, that links to the targeted item.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="target"/>
    /// is a null reference.</exception>
    public static DependencyNode<T> CreateDependency<T>(Expression<Func<T>> target)
    {
      return CreateDependency(target, null);
    }


    /// <summary>
    /// Creates dependency chain of <see cref="DependencyNode{T}"/> instances for a given
    /// expression.
    /// </summary>
    /// <typeparam name="T">The type of the expression's compiled return value.</typeparam>
    /// <param name="target">The expression that declares the dependency chain from root
    /// to the target item or value.</param>
    /// <param name="changeHandler">An optional event listener that is being invoked if the
    /// target value changes either through direct change, or because the dependency graph
    /// was changed because of a changed intermediary node.</param>
    /// <returns>The root dependency node, that links to the targeted item.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="target"/>
    /// is a null reference.</exception>
    public static DependencyNode<T> CreateDependency<T>(Expression<Func<T>> target,
                                                        EventHandler<DependencyChangeEventArgs<T>>  changeHandler)
    {
      if (target == null) throw new ArgumentNullException("target");
      
      //create settings component
      var settings = new DependencyNodeSettings<T>(target);
      if (changeHandler != null) settings.ChangeHandler = changeHandler;

      //delegate to overload
      return CreateDependency(settings);
    }



    /// <summary>
    /// Creates dependency chain of <see cref="DependencyNode{T}"/> instances for a given
    /// expression.
    /// </summary>
    /// <typeparam name="T">The type of the expression's compiled return value.</typeparam>
    /// <param name="settings">Settings that define the dependency graph, change listeners
    /// and monitoring behaviour.</param>
    /// <returns>The root dependency node, that links to the targeted item.</returns>
    public static DependencyNode<T> CreateDependency<T>(DependencyNodeSettings<T> settings)
    {
      DependencyNode<T> node = WalkExpressionTree<T>(settings.TargetExpression.Body, settings.ObserveSubValueChanges, null);
      if (settings.ChangeHandler != null) node.DependencyChanged += settings.ChangeHandler;
      return node;
    }




    /// <summary>
    /// Analyzes a given expression and returns the root <see cref="DependencyNode{T}"/>
    /// of the expression.
    /// </summary>
    /// <typeparam name="T">The type of the expression's compiled return value.</typeparam>
    /// <param name="expression">The expression to be analyzed.</param>
    /// <param name="observeSubValues">Whether an observed leaf item is monitored for changes of
    /// its properties, too.</param>
    /// <param name="childNode">A previously analyzed child node, if any.</param>
    /// <returns>The root node of the expression chain.</returns>
    private static DependencyNode<T> WalkExpressionTree<T>(Expression expression, bool observeSubValues, DependencyNode<T> childNode)
    {
      //construct new node and link to parent
      var node = new DependencyNode<T>(observeSubValues, expression);
      if (childNode != null) node.ChildNode = childNode;

      var memberExpression = expression as MemberExpression;
      if (memberExpression == null)
      {
        //check if we're dealing with an unary expression
        var unary = expression as UnaryExpression;
        if (unary != null) memberExpression = unary.Operand as MemberExpression;
      }
      
      if (memberExpression != null)
      {
        node.ParentMember = memberExpression.Member;
        node.ChildNode = childNode;
        return WalkExpressionTree(memberExpression.Expression, observeSubValues, node);
      }

      var constantExpression = expression as ConstantExpression;
      if (constantExpression != null)
      {
        //we're at the root of the tree
        node.NodeValue = constantExpression.Value;
        return node;
      }

      string msg = "Unsupported expression of type {0} submitted. Only expressions of type {1} and {2} are supported.";
      msg = String.Format(msg, expression.GetType().Name, typeof (MemberExpression).Name, typeof (ConstantExpression).Name);
      throw new InvalidOperationException(msg);
    }

  }
}